using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
        int? Installments);

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
        public async Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Request req, CancellationToken ct)
        {
            var references = await TransactionReferences.Load(db, req.AccountId, req.CategoryId, ct);
            if (!references.IsSuccess)
                return references.Error;

            var (account, category) = references.Value;
            var installments = req.Installments ?? 1;

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
