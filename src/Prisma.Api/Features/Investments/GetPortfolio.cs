using Microsoft.EntityFrameworkCore;
using Prisma.Api.Features.FixedIncome;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// A carteira em um ano (docs/investimentos.md, I.7e): o total investido, os grupos com o peso de cada um, os ativos, e o
// que o ano rendeu em proventos e vendas. Sem ano: o corrente. Years: do primeiro ano com nota, provento ou aporte em renda fixa
// até o corrente. A renda fixa (R.9) entra como um grupo só, pelo aportado até a data, com uma linha por objetivo.
public static class GetPortfolio
{
    public sealed record PortfolioGroupResponse(
        PortfolioGroup Group, long InvestedCents, int WeightBasisPoints, int AssetCount, long PayoutsCents, long RealizedCents);

    public sealed record PortfolioAssetResponse(
        Guid AssetId, string Symbol, string Name, AssetKind Kind, PortfolioGroup Group, bool HasLogo, bool IsActive, long Quantity,
        long GrossCostCents, long CostBasisCents, long? AveragePriceCents, long? AverageCostCents, int WeightBasisPoints,
        long PayoutsCents, long RealizedCents);

    // Uma linha da renda fixa: um objetivo (ou os aportes sem objetivo, GoalId e Name nulos) com o aportado, o rendimento bruto até a
    // data da carteira e o peso no total.
    public sealed record PortfolioFixedIncomeResponse(
        Guid? GoalId, string? Name, int DepositCount, long PrincipalCents, long GrossYieldCents, int WeightBasisPoints);

    public sealed record PortfolioResponse(
        int Year, DateOnly AsOf, IReadOnlyList<int> Years, long InvestedCents, long PayoutsCents, long RealizedCents,
        IReadOnlyList<PortfolioGroupResponse> Groups, IReadOnlyList<PortfolioAssetResponse> Assets,
        IReadOnlyList<PortfolioFixedIncomeResponse> FixedIncome, DateOnly? FixedIncomeRatesThrough);

    public sealed class Handler(AppDbContext db, IClock clock, GetBalances.Handler balances)
    {
        public async Task<Result<PortfolioResponse>> Execute(int? year, CancellationToken ct)
        {
            var today = clock.Today;
            var chosen = year ?? today.Year;

            var notes = await TradeBook.Notes(db, ct);
            var positions = await TradeBook.Positions(db, notes, ct);
            if (!positions.IsSuccess)
                return new Error(ErrorType.Conflict, $"As notas não fecham: {positions.Error.Message} Corrija a nota dessa venda.");

            // Só os do ano: o domínio confere a data de novo (até hoje no ano corrente). Fora de 1..9999 o domínio recusa.
            var (first, last) = (new DateOnly(Math.Clamp(chosen, 1, 9999), 1, 1), new DateOnly(Math.Clamp(chosen, 1, 9999), 12, 31));
            var payouts = await db.Transactions.AsNoTracking()
                .Where(t => t.AssetId != null && t.PurchaseDate >= first && t.PurchaseDate <= last)
                .Select(t => new PortfolioPayout(t.AssetId!.Value, t.PurchaseDate, t.AmountCents))
                .ToListAsync(ct);

            var assets = await TradeBook.Assets(db, positions.Value.Select(p => p.AssetId).Concat(payouts.Select(p => p.AssetId)), ct);
            // A renda fixa da data da carteira: aportado e rendimento bruto de cada objetivo, do mesmo cálculo da aba Objetivos. Ano fora
            // do intervalo aceito não chega ao domínio com a data inválida: o domínio recusa antes.
            var fixedIncome = new List<FixedIncomeHolding>();
            var names = new Dictionary<Guid, string>();
            DateOnly? ratesThrough = null;
            if (chosen >= PortfolioSnapshot.FirstYear && chosen <= today.Year)
            {
                var fixedBalances = await balances.Execute(PortfolioSnapshot.AsOfFor(chosen, today), ct);
                if (!fixedBalances.IsSuccess)
                    return fixedBalances.Error;

                foreach (var goal in fixedBalances.Value.Goals)
                {
                    names[goal.Id] = goal.Name;
                    fixedIncome.Add(new FixedIncomeHolding(goal.Id, OpenDeposits(goal.Deposits), goal.Balance.PrincipalCents, goal.Balance.GrossYieldCents));
                }

                var loose = fixedBalances.Value.Unassigned.Balance;
                fixedIncome.Add(new FixedIncomeHolding(null, OpenDeposits(fixedBalances.Value.Unassigned.Deposits), loose.PrincipalCents, loose.GrossYieldCents));
                ratesThrough = fixedBalances.Value.Total.RatesThrough;
            }

            var snapshot = PortfolioSnapshot.Build(
                chosen, today, positions.Value, payouts, id => assets[id].Kind, id => assets[id].Symbol, fixedIncome);
            if (!snapshot.IsSuccess)
                return snapshot.Error;

            var s = snapshot.Value;
            return new PortfolioResponse(s.Year, s.AsOf, await Years(notes, today, ct), s.InvestedCents, s.PayoutsCents, s.RealizedCents,
                s.Groups.Select(g => new PortfolioGroupResponse(g.Group, g.InvestedCents, g.WeightBasisPoints, g.AssetCount, g.PayoutsCents, g.RealizedCents)).ToList(),
                s.Assets.Select(a =>
                {
                    var asset = assets[a.AssetId];
                    var t = a.Totals;
                    return new PortfolioAssetResponse(a.AssetId, asset.Symbol, asset.Name, asset.Kind, a.Group, asset.HasLogo, asset.IsActive,
                        t.Quantity, t.GrossCostCents, t.CostBasisCents, t.AveragePriceCents, t.AverageCostCents, a.WeightBasisPoints,
                        a.PayoutsCents, a.RealizedCents);
                }).ToList(),
                s.FixedIncome.Select(f => new PortfolioFixedIncomeResponse(
                    f.GoalId, f.GoalId is { } id ? names[id] : null, f.DepositCount, f.PrincipalCents, f.GrossYieldCents, f.WeightBasisPoints)).ToList(),
                ratesThrough);
        }

        // Os aportes que ainda têm principal na data da carteira: o resgatado por inteiro deixou de ser um investimento (R.26). O principal e o
        // peso já vêm descontados dos resgates, porque saem do mesmo saldo da aba Objetivos.
        private static int OpenDeposits(IReadOnlyList<DepositBalance> deposits) => deposits.Count(d => d.PrincipalCents > 0);

        private async Task<IReadOnlyList<int>> Years(IReadOnlyList<TradeNote> notes, DateOnly today, CancellationToken ct)
        {
            var firstPayout = await db.Transactions.AsNoTracking().Where(t => t.AssetId != null)
                .MinAsync(t => (DateOnly?)t.PurchaseDate, ct);
            // As notas vêm em ordem de data: a primeira é a mais antiga.
            var firstDeposit = await db.FixedIncomeDeposits.AsNoTracking().MinAsync(d => (DateOnly?)d.DepositDate, ct);
            int[] starts =
            [
                today.Year, .. notes.Take(1).Select(n => n.Date.Year), .. firstPayout is { } d ? [d.Year] : Array.Empty<int>(),
                .. firstDeposit is { } f ? [f.Year] : Array.Empty<int>(),
            ];
            var first = starts.Min();
            return Enumerable.Range(first, today.Year - first + 1).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (int? year, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(year, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<PortfolioResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(409);
}
