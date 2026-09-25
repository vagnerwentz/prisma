using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class RestoreTransaction
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, CancellationToken ct)
        {
            // Única exceção da regra 6 do CLAUDE.md: ignora só o soft delete. O filtro de dono
            // continua valendo, então transação de outro usuário segue invisível.
            var transaction = await db.Transactions
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt != null, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação excluída não encontrada.");

            if (transaction.TransferPairId is not null)
                return await TransferPair.Restore(db, transaction, ct);

            if (transaction.CheckCanChangeIndividually() is { } error)
                return error;

            if (transaction.Type == TransactionType.Refund)
            {
                if (await CheckRefundCanBeRestored(transaction, ct) is { } refundError)
                    return refundError;
            }
            else if (transaction.StatementId is { } statementId)
            {
                var statements = await db.Statements.Where(s => s.Id == statementId).ToListAsync(ct);
                if (CardPurchase.CheckCanRestore([transaction], statements) is { } paidError)
                    return paidError;
            }

            if (!await db.Accounts.AnyAsync(a => a.Id == transaction.AccountId, ct))
                return new Error(ErrorType.Conflict,
                    "A conta desta transação foi excluída; não é possível restaurá-la.");

            var categoryStillExists = transaction.CategoryId is null
                || await db.Categories.AnyAsync(c => c.Id == transaction.CategoryId, ct);

            transaction.Restore(categoryStillExists);
            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(transaction);
        }

        // Restaurar o estorno segue as regras de criar (docs/fase-2.md, 2.5, regra 15): a fatura dele
        // não pode estar paga e, com a compra ainda ativa, o limite dela vale.
        private async Task<Error?> CheckRefundCanBeRestored(Transaction refund, CancellationToken ct)
        {
            var statements = refund.StatementId is { } statementId
                ? await db.Statements.Where(s => s.Id == statementId).ToListAsync(ct)
                : [];

            long? refundable = null;
            if (refund.RefundedTransactionId is { } purchaseId
                && await db.Transactions.SingleOrDefaultAsync(t => t.Id == purchaseId, ct) is { } purchase)
                refundable = (await RefundAmounts.Target(db, purchase, refund.Id, ct)).RefundableCents;

            return Refund.CheckCanRestore(refund, refundable, statements);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<TransactionResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
