using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.FixedIncome;

// O objetivo com o que a lista mostra dele: quantos aportes e quanto foi aportado. O rendimento e o saldo ficam para a consulta
// de saldo (docs/renda-fixa.md, R.5): nada daqui depende das taxas do CDI.
public sealed record GoalResponse(
    Guid Id, string Name, string? Description, long? TargetCents, long? DefaultIndexPercentE4, int DepositCount, long PrincipalCents);

// Os aportes que estão fora de qualquer caixa.
public sealed record UnassignedResponse(int DepositCount, long PrincipalCents);

public sealed record GoalsResponse(IReadOnlyList<GoalResponse> Goals, UnassignedResponse Unassigned);

// A transferência que financiou o aporte, quando o dinheiro saiu de uma conta pelo app e as pontas continuam vivas.
public sealed record FundingResponse(Guid TransferPairId, Guid FromAccountId, Guid InvestmentAccountId);

// RateE4 é a taxa contratada, e o que ela é depende do IndexKind: o percentual do CDI (Cdi) ou a taxa ao ano (CdiPlus, Prefixed).
// IndexPercentE4 é o nome antigo, só para uma tela aberta antes desta versão: traz o percentual nos aportes de CDI e nulo nos outros.
public sealed record DepositResponse(
    Guid Id, Guid? GoalId, DateOnly DepositDate, long AmountCents, FixedIncomeProductKind ProductKind, FixedIncomeIndexKind IndexKind,
    long RateE4, DateOnly? MaturityDate, DepositLiquidity Liquidity, string? Note, FundingResponse? Funding = null,
    long? IndexPercentE4 = null)
{
    public static DepositResponse From(FixedIncomeDeposit d, FundingResponse? funding = null) =>
        new(d.Id, d.GoalId, d.DepositDate, d.AmountCents, d.ProductKind, d.IndexKind, d.RateE4, d.MaturityDate, d.Liquidity,
            d.Note, funding, LegacyPercent(d));

    // O nome antigo do campo: só no CDI, onde a taxa é o percentual.
    public static long? LegacyPercent(FixedIncomeDeposit d) => d.IndexKind == FixedIncomeIndexKind.Cdi ? d.RateE4 : null;
}

// O financiamento dos aportes (docs/renda-fixa.md, D3): a transferência que tirou o dinheiro de uma conta e o pôs na conta de
// investimento. Vive em duas linhas de transação, que a tela de lançamentos também enxerga, exclui e restaura.
public static class Fundings
{
    // Os financiamentos vivos de um conjunto de aportes. Aporte sem par, ou com as pontas excluídas, fica sem financiamento.
    public static async Task<IReadOnlyList<DepositResponse>> Respond(
        AppDbContext db, IReadOnlyCollection<FixedIncomeDeposit> deposits, CancellationToken ct)
    {
        var pairs = deposits.Where(d => d.TransferPairId is not null).Select(d => d.TransferPairId!.Value).Distinct().ToList();
        var live = new Dictionary<Guid, FundingResponse>();
        if (pairs.Count > 0)
        {
            var legs = await db.Transactions.AsNoTracking()
                .Where(t => t.TransferPairId != null && pairs.Contains(t.TransferPairId.Value))
                .Select(t => new { Pair = t.TransferPairId!.Value, t.AccountId, t.TransferDirection })
                .ToListAsync(ct);
            foreach (var group in legs.GroupBy(l => l.Pair))
            {
                var from = group.SingleOrDefault(l => l.TransferDirection == TransferDirection.Out);
                var to = group.SingleOrDefault(l => l.TransferDirection == TransferDirection.In);
                if (from is not null && to is not null)
                    live[group.Key] = new FundingResponse(group.Key, from.AccountId, to.AccountId);
            }
        }

        return deposits
            .Select(d => DepositResponse.From(d, d.TransferPairId is { } pair && live.TryGetValue(pair, out var funding) ? funding : null))
            .ToList();
    }

    public static async Task<DepositResponse> Respond(AppDbContext db, FixedIncomeDeposit deposit, CancellationToken ct) =>
        (await Respond(db, [deposit], ct))[0];

    // As pontas vivas do par (as duas, ou nenhuma).
    public static Task<List<Transaction>> LiveLegs(AppDbContext db, Guid pairId, CancellationToken ct) =>
        db.Transactions.Where(t => t.TransferPairId == pairId).ToListAsync(ct);

    // As pontas do par que ainda existem no banco, vivas ou excluídas (o "Desfazer" vale 30 minutos). Sem nenhuma, o par foi
    // apagado de vez e o aporte volta a ser só acompanhado.
    public static Task<bool> AnyLegExists(AppDbContext db, Guid pairId, CancellationToken ct) =>
        db.Transactions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AnyAsync(t => t.TransferPairId == pairId, ct);
}

public static class Goals
{
    // Os números de um objetivo: o banco soma, o domínio não entra (é só contagem e soma de principal).
    public static async Task<GoalResponse> Summary(AppDbContext db, SavingsGoal goal, CancellationToken ct)
    {
        var totals = await db.FixedIncomeDeposits.AsNoTracking()
            .Where(d => d.GoalId == goal.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Principal = g.Sum(d => d.AmountCents) })
            .SingleOrDefaultAsync(ct);
        return new GoalResponse(goal.Id, goal.Name, goal.Description, goal.TargetCents, goal.DefaultIndexPercentE4,
            totals?.Count ?? 0, totals?.Principal ?? 0);
    }

    // O objetivo existe (e é da pessoa: o filtro de dono vale) e não foi excluído.
    public static Task<bool> Exists(AppDbContext db, Guid goalId, CancellationToken ct) =>
        db.SavingsGoals.AnyAsync(g => g.Id == goalId, ct);
}
