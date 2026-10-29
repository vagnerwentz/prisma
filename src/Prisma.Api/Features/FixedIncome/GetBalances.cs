using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.FixedIncome;

// O aporte com o saldo bruto calculado até a data de referência. Sem taxa para a data do aporte (histórico que não chega até ela),
// os campos de saldo vêm nulos e Unavailable explica: nunca zero no lugar de taxa que falta.
public sealed record DepositBalance(
    Guid DepositId, Guid? GoalId, DateOnly DepositDate, long PrincipalCents, FixedIncomeProductKind ProductKind, FixedIncomeIndexKind IndexKind, long RateE4,
    long? GrossBalanceCents, long? GrossYieldCents, long? FactorE8, int? RatesUsed, DateOnly? RatesThrough, string? Unavailable,
    DepositTax? Tax, long? IndexPercentE4 = null, DepositRedeemed? Redeemed = null);

// O que já foi resgatado do aporte até a data de referência (R.23): bruto, principal, rendimento, imposto pago e líquido recebido, somados
// dos resgates gravados. Nulo no aporte sem resgate. Com ele, `PrincipalCents` do aporte é o que sobra, e o saldo é só o do que sobra.
public sealed record DepositRedeemed(int Count, long GrossCents, long PrincipalCents, long YieldCents, long TaxCents, long NetCents);

// O imposto estimado do aporte se ele fosse resgatado na data de referência, e o que sobra depois dele (R.7). IOF e IR em centavos,
// a alíquota do IR em pontos-base (2250 = 22,5 %), o prazo em dias corridos e a versão da regra que valeu.
public sealed record DepositTax(
    long IofCents, long IrCents, long TotalCents, int IrRateBasisPoints, int HoldingDays, string RuleVersion, long NetYieldCents,
    long NetBalanceCents);

// A soma de um grupo (uma caixa, os soltos ou tudo). O principal soma todos os aportes. Aporte sem taxa entra no saldo pelo
// principal (rendimento zero, e é contado em UnavailableCount): o total nunca some com dinheiro que a pessoa aportou, e o aviso diz
// que está incompleto.
public sealed record GroupBalance(
    int DepositCount, int UnavailableCount, long PrincipalCents, long GrossBalanceCents, long GrossYieldCents, DateOnly? RatesThrough,
    long EstimatedTaxCents, long NetYieldCents, long NetBalanceCents, long RedeemedGrossCents = 0, long RedeemedYieldCents = 0,
    long RedeemedTaxCents = 0, long RedeemedNetCents = 0);

public sealed record GoalBalance(
    Guid Id, string Name, long? TargetCents, GroupBalance Balance, IReadOnlyList<DepositBalance> Deposits);

public sealed record LooseBalance(GroupBalance Balance, IReadOnlyList<DepositBalance> Deposits);

// O rendimento bruto do que sobra dos aportes que entraram numa conta de investimento (D20): o saldo da conta guarda só o principal, e
// com este número a tela mostra o que está investido naquela conta. Só aporte financiado por conta, com as duas pontas vivas.
// PrincipalCents (I.9): o principal que sobra desses aportes, depois dos resgates; está no saldo da conta e também em "Investido" pela
// carteira, então o caixa livre da corretora o desconta.
public sealed record AccountYield(Guid AccountId, int DepositCount, long GrossYieldCents, long PrincipalCents = 0);

public sealed record BalancesResponse(
    DateOnly AsOf, RatesStatus Rates, IReadOnlyList<GoalBalance> Goals, LooseBalance Unassigned, GroupBalance Total,
    IReadOnlyList<AccountYield> ByAccount);

// O saldo de cada aporte, de cada caixa e de tudo, até uma data (docs/renda-fixa.md, 6). É o
// total aportado, o rendimento calculado das taxas do CDI publicadas e a data até onde elas vão, nunca uma projeção: data de
// referência no futuro é recusada. Aporte feito depois da data de referência ainda não existe nela e fica de fora.
// O líquido (R.7) é o bruto menos o imposto estimado de resgatar naquela data, aporte a aporte (cada um tem a sua faixa de IR).
public static class GetBalances
{
    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<BalancesResponse>> Execute(DateOnly? asOfRequest, CancellationToken ct)
        {
            var today = clock.Today;
            var asOf = asOfRequest ?? today;
            if (asOf > today)
                return new Error(ErrorType.Validation, "A data de referência não pode estar no futuro: o saldo é o que já rendeu.");

            var goals = await db.SavingsGoals.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var deposits = await db.FixedIncomeDeposits.AsNoTracking().Where(d => d.DepositDate <= asOf).ToListAsync(ct);
            var book = await RateBook.Load(db, clock, ct);
            var depositIds = deposits.Select(d => d.Id).ToList();
            var redemptions = (await db.FixedIncomeRedemptions.AsNoTracking()
                    .Where(r => depositIds.Contains(r.DepositId) && r.RedemptionDate <= asOf).ToListAsync(ct))
                .ToLookup(r => r.DepositId);

            var balances = deposits
                .Select(d => (Deposit: d, Balance: Balance(d, book, asOf, redemptions[d.Id].ToList())))
                .OrderByDescending(x => x.Deposit.DepositDate).ThenByDescending(x => x.Deposit.CreatedAt)
                .ToList();

            var byGoal = balances.Where(x => x.Deposit.GoalId is not null).GroupBy(x => x.Deposit.GoalId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Balance).ToList());
            var loose = balances.Where(x => x.Deposit.GoalId is null).Select(x => x.Balance).ToList();

            var pairs = deposits.Where(d => d.TransferPairId is not null).Select(d => d.TransferPairId!.Value).Distinct().ToList();
            var live = await Fundings.LiveFundings(db, pairs, ct);
            var byAccount = balances
                .Where(x => x.Deposit.TransferPairId is { } pair && live.ContainsKey(pair))
                .GroupBy(x => live[x.Deposit.TransferPairId!.Value].InvestmentAccountId)
                .Select(g => new AccountYield(g.Key, g.Count(), g.Sum(x => x.Balance.GrossYieldCents ?? 0), g.Sum(x => x.Balance.PrincipalCents)))
                .OrderBy(a => a.AccountId)
                .ToList();

            return new BalancesResponse(
                asOf,
                book.Status,
                goals.Select(g =>
                {
                    var inside = byGoal.GetValueOrDefault(g.Id) ?? [];
                    return new GoalBalance(g.Id, g.Name, g.TargetCents, Sum(inside), inside);
                }).ToList(),
                new LooseBalance(Sum(loose), loose),
                Sum(balances.Select(x => x.Balance).ToList()),
                byAccount);
        }

        private static DepositBalance Balance(
            FixedIncomeDeposit deposit, RateBook book, DateOnly asOf, IReadOnlyList<FixedIncomeRedemption> redemptions)
        {
            var steps = redemptions.Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList();
            DepositRedeemed? redeemed = redemptions.Count == 0
                ? null
                : new DepositRedeemed(
                    redemptions.Count, redemptions.Sum(r => r.GrossCents), redemptions.Sum(r => r.PrincipalCents),
                    redemptions.Sum(r => r.YieldCents), redemptions.Sum(r => r.TaxCents), redemptions.Sum(r => r.NetCents));
            var result = FixedIncomeEngine.Balance(
                new Money(deposit.AmountCents), deposit.DepositDate, deposit.Remuneration, book.History, asOf, steps);
            if (!result.IsSuccess)
                return new DepositBalance(
                    deposit.Id, deposit.GoalId, deposit.DepositDate, deposit.AmountCents - (redeemed?.PrincipalCents ?? 0), deposit.ProductKind,
                    deposit.IndexKind, deposit.RateE4, null, null, null, null, null, result.Error.Code ?? "Unavailable", null,
                    DepositResponse.LegacyPercent(deposit), redeemed);

            var b = result.Value;
            var tax = FixedIncomeTax.Estimate(deposit.ProductKind, deposit.DepositDate, asOf, b.GrossYield);
            DepositTax? estimated = null;
            if (tax.IsSuccess)
            {
                var (netYield, netBalance) = FixedIncomeTax.Net(b.GrossBalance, b.GrossYield, tax.Value);
                estimated = new DepositTax(
                    tax.Value.IofCents, tax.Value.IrCents, tax.Value.TotalCents, tax.Value.IrRateBasisPoints, tax.Value.HoldingDays,
                    tax.Value.RuleVersion, netYield.Cents, netBalance.Cents);
            }

            return new DepositBalance(
                deposit.Id, deposit.GoalId, deposit.DepositDate, b.Principal.Cents, deposit.ProductKind, deposit.IndexKind, deposit.RateE4,
                b.GrossBalance.Cents, b.GrossYield.Cents, b.FactorE8, b.RatesUsed, b.RatesThrough, null, estimated,
                DepositResponse.LegacyPercent(deposit), redeemed);
        }

        private static GroupBalance Sum(IReadOnlyList<DepositBalance> items)
        {
            long principal = 0, gross = 0, tax = 0, redeemedGross = 0, redeemedYield = 0, redeemedTax = 0, redeemedNet = 0;
            var unavailable = 0;
            DateOnly? through = null;
            foreach (var item in items)
            {
                principal += item.PrincipalCents;
                redeemedGross += item.Redeemed?.GrossCents ?? 0;
                redeemedYield += item.Redeemed?.YieldCents ?? 0;
                redeemedTax += item.Redeemed?.TaxCents ?? 0;
                redeemedNet += item.Redeemed?.NetCents ?? 0;
                if (item.GrossBalanceCents is { } balance)
                {
                    gross += balance;
                    tax += item.Tax?.TotalCents ?? 0;
                    // O grupo só vale até onde todos os aportes valem: a data mais antiga entre as últimas taxas usadas.
                    if (item.RatesThrough is { } date && (through is null || date < through))
                        through = date;
                }
                else
                {
                    gross += item.PrincipalCents;
                    unavailable++;
                }
            }

            return new GroupBalance(items.Count, unavailable, principal, gross, gross - principal, through, tax, gross - principal - tax, gross - tax,
                redeemedGross, redeemedYield, redeemedTax, redeemedNet);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/balances", async (DateOnly? asOf, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(asOf, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<BalancesResponse>(200)
            .ProducesProblem(400);
}
