using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
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

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
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
            var previousAccount = transaction.AccountId;

            if (transaction.Type == TransactionType.Refund)
                return await UpdateRefund(transaction, req, account, category, ct);

            // Despesa já estornada: tipo e conta ficam, e o valor não desce abaixo do estornado
            // (docs/fase-2.md, 2.5, regra 7). A parcela isolada não muda de valor nem de conta.
            if (transaction.Type == TransactionType.Expense && transaction.InstallmentPurchaseId is null)
            {
                var refunded = await RefundAmounts.RefundedOf(db, transaction, exceptRefundId: null, ct);
                if (Refund.CheckPurchaseEdit(transaction, req.Type, account.Id, req.AmountCents, refunded) is { } refundError)
                    return refundError;
            }

            if (transaction.StatementId is null)
            {
                var updated = transaction.UpdateSimple(
                    account, req.Type, req.AmountCents, req.PurchaseDate, category, req.Method, req.Description);
                if (!updated.IsSuccess)
                    return updated.Error;
            }
            else
            {
                // No cartão, a data ou o cartão novos podem levar a compra para outra fatura (etapa 1.14b;
                // docs/fase-2.md, 2.13). Na troca, vêm as faturas dos dois cartões.
                var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == transaction.AccountId, ct);
                if (card is null)
                    return new Error(ErrorType.Conflict, "A conta desta compra foi excluída.");

                var from = Min(transaction.PurchaseDate, req.PurchaseDate).AddMonths(-2);
                var statements = await db.Statements
                    .Where(s => (s.AccountId == card.Id || s.AccountId == account.Id)
                        && (s.ClosingDate >= from || s.Id == transaction.StatementId))
                    .ToListAsync(ct);
                var source = transaction.StatementId;

                var edited = CardPurchase.EditTransaction(
                    transaction, card, statements, account, req.Type, req.AmountCents, req.PurchaseDate,
                    category, req.Method, req.Description);
                if (!edited.IsSuccess)
                    return edited.Error;

                db.Statements.AddRange(edited.Value);
                // Pôr e tirar compra de fatura conta como mudança nas duas (CLAUDE.md, seção 6).
                if (transaction.StatementId != source)
                    StatementTouch.Touch(db, statements.Where(s => s.Id == source || s.Id == transaction.StatementId));
            }

            await db.SaveChangesAsync(ct);
            if (transaction.StatementId is not null && transaction.AccountId != previousAccount)
                logger.PurchaseMovedToCard(transaction.Id, previousAccount, transaction.AccountId);
            return TransactionResponse.From(transaction);
        }

        // Estorno: valor, data, categoria, descrição e meio; conta e vínculo não mudam (regra 14).
        private async Task<Result<TransactionResponse>> UpdateRefund(
            Transaction refund, Request req, Account account, Category? category, CancellationToken ct)
        {
            if (req.Type != TransactionType.Refund)
                return new Error(ErrorType.Validation, "No estorno, o tipo não muda. Exclua e lance de novo.");

            // Compra ainda ativa: o limite conta os outros estornos dela, não este.
            long? refundable = null;
            if (refund.RefundedTransactionId is { } purchaseId
                && await db.Transactions.SingleOrDefaultAsync(t => t.Id == purchaseId, ct) is { } purchase)
                refundable = (await RefundAmounts.Target(db, purchase, refund.Id, ct)).RefundableCents;

            List<Statement> statements = [];
            if (refund.StatementId is not null)
            {
                var from = Min(refund.PurchaseDate, req.PurchaseDate).AddMonths(-2);
                statements = await db.Statements
                    .Where(s => s.AccountId == refund.AccountId && (s.ClosingDate >= from || s.Id == refund.StatementId))
                    .ToListAsync(ct);
            }

            var source = refund.StatementId;
            var edited = Refund.Edit(
                refund, account, req.AmountCents, req.PurchaseDate, category, req.Method, req.Description, refundable, statements);
            if (!edited.IsSuccess)
                return edited.Error;

            db.Statements.AddRange(edited.Value);
            if (refund.StatementId != source)
                StatementTouch.Touch(db, statements.Where(s => s.Id == source || s.Id == refund.StatementId));
            await db.SaveChangesAsync(ct);
            return TransactionResponse.From(refund);
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
            .ProducesProblem(404)
            .ProducesProblem(409);
}
