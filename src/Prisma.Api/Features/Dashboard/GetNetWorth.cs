using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.Accounts;
using Prisma.Api.Features.FixedIncome;
using Prisma.Api.Features.Investments;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Features.Dashboard;

// O patrimônio de hoje em três partes (I.9, docs/patrimonio.md): o dinheiro livre, o investido e o caixa das corretoras. O "Investido"
// vem dos ativos (a carteira de hoje e o rendimento da renda fixa), seja qual for o histórico de caixa; a conta de investimento só entra
// se a pessoa acompanha o caixa dela. A regra é do domínio (NetWorthSplit); aqui só se juntam os números que já existem. Nada é gravado.
public static class GetNetWorth
{
    // Uma conta de investimento ativa: se acompanha o caixa e, se sim, quanto sobra livre nela (negativo se o histórico não fecha).
    public sealed record BrokerAccount(Guid AccountId, bool TracksCash, long? FreeCashCents);

    public sealed record Response(
        long AvailableCents, int AvailableCount, long InvestedCents, long YieldCents, long BrokerCashCents, long TotalCents,
        IReadOnlyList<BrokerAccount> Brokers);

    public sealed class Handler(
        AppDbContext db, ListAccountBalances.Handler balances, GetBalances.Handler fixedIncome, GetPortfolio.Handler portfolio)
    {
        public async Task<Result<Response>> Execute(CancellationToken ct)
        {
            var wallet = await portfolio.Execute(null, ct);
            if (!wallet.IsSuccess)
                return wallet.Error;

            var rates = await fixedIncome.Execute(null, ct);
            if (!rates.IsSuccess)
                return rates.Error;

            var accounts = await db.Accounts.AsNoTracking()
                .Where(a => a.IsActive && a.Type != AccountType.CreditCard)
                .Select(a => new { a.Id, a.Type, a.TracksCash })
                .ToListAsync(ct);
            var balanceOf = (await balances.Execute(ct)).ToDictionary(b => b.AccountId, b => b.BalanceCents ?? 0);
            var principalOf = rates.Value.ByAccount.ToDictionary(y => y.AccountId, y => y.PrincipalCents);

            var rows = accounts
                .Select(a => (
                    Account: a,
                    Input: new NetWorthAccount(a.Type, true, a.TracksCash, balanceOf.GetValueOrDefault(a.Id), principalOf.GetValueOrDefault(a.Id))))
                .ToList();

            // O rendimento bruto de toda a renda fixa, não só o dos aportes com trilha de caixa: é o mesmo da Carteira.
            var yield = wallet.Value.FixedIncome.Sum(l => l.GrossYieldCents);
            var split = NetWorthSplit.Of(rows.Select(r => r.Input), wallet.Value.InvestedCents, yield);

            var brokers = rows
                .Where(r => r.Account.Type == AccountType.Investment)
                .Select(r => new BrokerAccount(r.Account.Id, r.Account.TracksCash, r.Account.TracksCash ? r.Input.FreeCashCents : null))
                .ToList();

            return new Response(
                split.AvailableCents, rows.Count(r => r.Account.Type is AccountType.Checking or AccountType.Cash), split.InvestedCents,
                yield, split.BrokerCashCents, split.TotalCents, brokers);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/net-worth", async (Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<Response>(200)
            .ProducesProblem(409);
}
