using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Accounts;

// Saldo de cada conta (docs/fase-1.md, 2.5). O banco soma agrupado; a regra de sinal e o limite
// disponível ficam no domínio. Conta que não é cartão traz o saldo; cartão, o que falta pagar.
public static class ListAccountBalances
{
    public sealed record Response(
        Guid AccountId,
        long? BalanceCents,
        long? ProjectedBalanceCents,
        long? OwedCents,
        long? AvailableCreditCents);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<IReadOnlyList<Response>> Execute(CancellationToken ct)
        {
            var today = clock.Today;

            var accounts = await db.Accounts
                .AsNoTracking()
                .Select(a => new { a.Id, a.Type, a.InitialBalanceCents, a.CreditLimitCents })
                .ToListAsync(ct);

            var sums = await db.Transactions
                .AsNoTracking()
                .GroupBy(t => new { t.AccountId, t.Type, t.TransferDirection, IsSettled = t.SettlementDate <= today })
                .Select(g => new { g.Key.AccountId, g.Key.Type, g.Key.TransferDirection, g.Key.IsSettled, Cents = g.Sum(t => t.AmountCents) })
                .ToListAsync(ct);

            // Total de cada fatura não paga (compras − estornos; o pagamento é Transfer e fica de fora).
            // O domínio conta cada uma a partir de zero: saldo a favor não abate as outras
            // (docs/fase-2.md, 2.5, regras 9 e 10).
            var unpaid = (await db.Statements
                .AsNoTracking()
                .Where(s => !s.IsPaid)
                .Select(s => new
                {
                    s.AccountId,
                    TotalCents = db.Transactions.Where(t => t.StatementId == s.Id && t.Type != TransactionType.Transfer)
                        .Sum(t => t.Type == TransactionType.Refund ? -t.AmountCents : t.AmountCents),
                })
                .ToListAsync(ct))
                .ToLookup(s => s.AccountId, s => s.TotalCents);

            return accounts.Select(a =>
            {
                if (a.Type == AccountType.CreditCard)
                {
                    var card = CreditCardBalance.OfStatements(a.CreditLimitCents, unpaid[a.Id]);
                    return new Response(a.Id, null, null, card.OwedCents, card.AvailableCreditCents);
                }

                var entries = sums
                    .Where(s => s.AccountId == a.Id)
                    .Select(s => new BalanceEntry(s.Type, s.TransferDirection, s.Cents, s.IsSettled));
                var balance = AccountBalance.Of(a.InitialBalanceCents, entries);
                return new Response(a.Id, balance.CurrentCents, balance.ProjectedCents, null, null);
            }).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/balances", async (Handler handler, CancellationToken ct) =>
            Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<Response>>(200);
}
