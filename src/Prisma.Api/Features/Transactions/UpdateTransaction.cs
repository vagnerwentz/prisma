using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class UpdateTransaction
{
    // Todos os campos editáveis juntos, como nos demais PATCH.
    public sealed record Request(
        Guid AccountId,
        TransactionType Type,
        long AmountCents,
        DateOnly PurchaseDate,
        Guid? CategoryId,
        PaymentMethod Method,
        string? Description);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Type).IsInEnum().WithMessage("Tipo de transação inválido.");
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
        }
    }

    public sealed class Handler(AppDbContext db)
    {
        public Task<Result<TransactionResponse>> Execute(Guid id, Request req, CancellationToken ct) =>
            ConcurrentStatementOpening.Retry(db, () => Update(id, req, ct));

        private async Task<Result<TransactionResponse>> Update(Guid id, Request req, CancellationToken ct)
        {
            var transaction = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;

            if (transaction.StatementId is null)
            {
                var updated = transaction.UpdateSimple(
                    account, req.Type, req.AmountCents, req.PurchaseDate, category, req.Method, req.Description);
                if (!updated.IsSuccess)
                    return updated.Error;
            }
            else
            {
                // No cartão, a data nova pode levar a compra para outra fatura (etapa 1.14b).
                var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == transaction.AccountId, ct);
                if (card is null)
                    return new Error(ErrorType.Conflict, "A conta desta compra foi excluída.");

                var from = Min(transaction.PurchaseDate, req.PurchaseDate).AddMonths(-2);
                var statements = await db.Statements
                    .Where(s => s.AccountId == card.Id && (s.ClosingDate >= from || s.Id == transaction.StatementId))
                    .ToListAsync(ct);

                var edited = CardPurchase.EditTransaction(
                    transaction, card, statements, account, req.Type, req.AmountCents, req.PurchaseDate,
                    category, req.Method, req.Description);
                if (!edited.IsSuccess)
                    return edited.Error;

                db.Statements.AddRange(edited.Value);
            }

            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(transaction);
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<TransactionResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404);
}
