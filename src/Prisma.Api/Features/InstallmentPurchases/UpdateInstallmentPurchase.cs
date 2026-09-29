using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Features.Transactions;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.InstallmentPurchases;

public static class UpdateInstallmentPurchase
{
    // Todos os campos editáveis juntos, como nos demais PATCH. Mudar a data leva as parcelas para
    // as faturas dos ciclos da nova data (etapa 1.14b); o cartão (AccountId), para as do cartão novo
    // (docs/fase-2.md, 2.13). Sem AccountId, o cartão fica: a tela aberta antes do deploy da 2.24 não o manda.
    public sealed record Request(
        long TotalAmountCents, int InstallmentCount, Guid? CategoryId, string? Description, DateOnly PurchaseDate,
        Guid? AccountId = null);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(x => x.PurchaseDate).NotEmpty().WithMessage("Informe a data da compra.");
    }

    public sealed record Response(
        Guid Id,
        Guid AccountId,
        string Description,
        long TotalAmountCents,
        int InstallmentCount,
        DateOnly PurchaseDate,
        IReadOnlyList<TransactionResponse> Installments);

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public Task<Result<Response>> Execute(Guid id, Request req, CancellationToken ct) =>
            ConcurrentStatementOpening.Retry(db, () => Update(id, req, ct));

        private async Task<Result<Response>> Update(Guid id, Request req, CancellationToken ct)
        {
            var purchase = await db.InstallmentPurchases.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (purchase is null)
                return new Error(ErrorType.NotFound, "Compra parcelada não encontrada.");

            var card = await db.Accounts.SingleOrDefaultAsync(a => a.Id == purchase.AccountId, ct);
            if (card is null)
                return new Error(ErrorType.Conflict, "A conta desta compra foi excluída.");

            var newCard = card;
            if (req.AccountId is { } accountId && accountId != card.Id)
            {
                newCard = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
                if (newCard is null)
                    return new Error(ErrorType.Validation, "Conta não encontrada.");
            }

            Category? category = null;
            if (req.CategoryId is { } categoryId)
            {
                category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
                if (category is null)
                    return new Error(ErrorType.Validation, "Categoria não encontrada.");
            }

            var installments = await db.Transactions.Where(t => t.InstallmentPurchaseId == id).ToListAsync(ct);

            // Compra já estornada não troca de cartão nem fica menor do que o estornado (docs/fase-2.md,
            // 2.5, regra 7; 2.13, regra 4).
            if (installments.Count > 0)
            {
                var refunded = await RefundAmounts.RefundedOf(db, installments[0], exceptRefundId: null, ct);
                if (Refund.CheckPurchaseEdit(installments[0], TransactionType.Expense, newCard.Id, req.TotalAmountCents, refunded)
                    is { } refundError)
                    return refundError;
            }

            // As faturas das parcelas (para saber quais estão pagas) e as que podem receber
            // parcelas: o cálculo parte de um ciclo antes do mês da compra, antiga ou nova.
            var statementIds = installments.Select(t => t.StatementId).ToList();
            var earliest = req.PurchaseDate < purchase.PurchaseDate ? req.PurchaseDate : purchase.PurchaseDate;
            var from = earliest.AddMonths(-2);
            var statements = await db.Statements
                .Where(s => (s.AccountId == card.Id || s.AccountId == newCard.Id)
                    && (s.ClosingDate >= from || statementIds.Contains(s.Id)))
                .ToListAsync(ct);
            var sources = installments.ToDictionary(t => t, t => t.StatementId);

            var edited = CardPurchase.Edit(
                purchase, card, installments, statements, req.TotalAmountCents, req.InstallmentCount,
                category, req.Description, req.PurchaseDate, newCard);
            if (!edited.IsSuccess)
                return edited.Error;

            db.Statements.AddRange(edited.Value.OpenedStatements);
            db.Transactions.AddRange(edited.Value.Added);
            db.Transactions.RemoveRange(edited.Value.Removed);

            // Pôr e tirar parcela de fatura conta como mudança nas duas (CLAUDE.md, seção 6): a que mudou
            // de fatura, a acrescentada e a removida.
            var touched = installments.Where(t => t.StatementId != sources[t]).SelectMany(t => new[] { sources[t], t.StatementId })
                .Concat(edited.Value.Added.Select(t => t.StatementId))
                .Concat(edited.Value.Removed.Select(t => t.StatementId))
                .ToHashSet();
            StatementTouch.Touch(db, statements.Where(s => touched.Contains(s.Id)));
            await db.SaveChangesAsync(ct);
            if (newCard.Id != card.Id)
                logger.PurchaseMovedToCard(purchase.Id, card.Id, newCard.Id);

            var current = installments.Except(edited.Value.Removed).Concat(edited.Value.Added)
                .OrderBy(t => t.InstallmentNumber)
                .Select(TransactionResponse.From)
                .ToList();

            return new Response(
                purchase.Id, purchase.AccountId, purchase.Description, purchase.TotalAmountCents,
                purchase.InstallmentCount, purchase.PurchaseDate, current);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/installment-purchases/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<Response>(200)
            .ProducesValidationProblem()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
