using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Statements;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public static class CreateTransaction
{
    // Em conta de cartão, AmountCents é o valor total da compra e Installments o número de
    // parcelas (1 = à vista). Fora do cartão, Installments precisa ser 1.
    public sealed record Request(
        Guid AccountId,
        TransactionType Type,
        long AmountCents,
        DateOnly PurchaseDate,
        Guid? CategoryId,
        PaymentMethod Method,
        string? Description,
        int? Installments,
        // Só no estorno: a compra que ele devolve (docs/fase-2.md, 2.5, regra 7).
        Guid? RefundedTransactionId = null);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Type).IsInEnum().WithMessage("Tipo de transação inválido.");
            RuleFor(x => x.Method).IsInEnum().WithMessage("Meio de pagamento inválido.");
        }
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        // Sempre uma lista: uma transação no lançamento simples, uma por parcela no cartão.
        public Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Request req, CancellationToken ct) =>
            ConcurrentStatementOpening.Retry(db, () => Create(req, ct));

        private async Task<Result<IReadOnlyList<TransactionResponse>>> Create(Request req, CancellationToken ct)
        {
            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;
            var installments = req.Installments ?? 1;

            if (req.Type == TransactionType.Refund)
                return await CreateRefund(req, account, category, installments, ct);

            if (req.RefundedTransactionId is not null)
                return new Error(ErrorType.Validation, "Só o estorno aponta a compra estornada.");

            if (account.Type == AccountType.CreditCard)
                return await CreateCardPurchase(req, account, category, installments, ct);

            if (installments != 1)
                return CardPurchase.InstallmentsRequireCard;

            var created = Transaction.CreateSimple(
                currentUser.UserId, account, req.Type, req.AmountCents, req.PurchaseDate,
                category, req.Method, req.Description);
            if (!created.IsSuccess)
                return created.Error;

            db.Transactions.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return new[] { TransactionResponse.From(created.Value) };
        }

        private async Task<Result<IReadOnlyList<TransactionResponse>>> CreateCardPurchase(
            Request req, Account card, Domain.Categories.Category? category, int installments, CancellationToken ct)
        {
            // O cálculo parte de um ciclo antes do mês da compra; faturas mais antigas não influem.
            var from = req.PurchaseDate.AddMonths(-2);
            var existing = await db.Statements
                .Where(s => s.AccountId == card.Id && s.ClosingDate >= from)
                .ToListAsync(ct);

            var purchase = CardPurchase.Create(
                currentUser.UserId, card, req.Type, req.Method, req.AmountCents, installments,
                req.PurchaseDate, category, req.Description, existing);
            if (!purchase.IsSuccess)
                return purchase.Error;

            if (purchase.Value.Purchase is { } installmentPurchase)
                db.InstallmentPurchases.Add(installmentPurchase);
            db.Statements.AddRange(purchase.Value.OpenedStatements);
            db.Transactions.AddRange(purchase.Value.Installments);
            await db.SaveChangesAsync(ct);

            return purchase.Value.Installments.Select(TransactionResponse.From).ToList();
        }

        // Estorno (docs/fase-2.md, 2.5): no cartão, entra na fatura aberta na data do estorno.
        private async Task<Result<IReadOnlyList<TransactionResponse>>> CreateRefund(
            Request req, Account account, Domain.Categories.Category? category, int installments, CancellationToken ct)
        {
            if (installments != 1)
                return new Error(ErrorType.Validation, "Estorno não tem parcelas.");

            RefundTarget? target = null;
            if (req.RefundedTransactionId is { } purchaseId)
            {
                var purchase = await db.Transactions.SingleOrDefaultAsync(t => t.Id == purchaseId, ct);
                if (purchase is null)
                    return new Error(ErrorType.Validation, "Compra estornada não encontrada.");
                target = await RefundAmounts.Target(db, purchase, exceptRefundId: null, ct);
            }

            // Como na compra: o cálculo parte de um ciclo antes do mês do estorno.
            var from = req.PurchaseDate.AddMonths(-2);
            var statements = account.Type == AccountType.CreditCard
                ? await db.Statements.Where(s => s.AccountId == account.Id && s.ClosingDate >= from).ToListAsync(ct)
                : [];

            var refund = Refund.Create(
                currentUser.UserId, account, req.AmountCents, req.PurchaseDate, category, req.Method,
                req.Description, target, statements);
            if (!refund.IsSuccess)
                return refund.Error;

            db.Statements.AddRange(refund.Value.OpenedStatements);
            db.Transactions.Add(refund.Value.Refund);
            await db.SaveChangesAsync(ct);
            return new[] { TransactionResponse.From(refund.Value.Refund) };
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess
                    ? Results.Created((string?)null, result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<IReadOnlyList<TransactionResponse>>(201)
            .ProducesValidationProblem();
}
