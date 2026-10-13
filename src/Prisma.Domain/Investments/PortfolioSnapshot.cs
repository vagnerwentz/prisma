using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Domain.Investments;

// A carteira vista por grupo (docs/investimentos.md, I.7e). A ordem é a dos grupos no desempate.
public enum PortfolioGroup { Stocks, Fiis, Etfs, Bdrs, OtherFunds, Other }

public static class PortfolioGroups
{
    // Unit fica com as ações; os fundos menores juntos (decisão do dono, 2026-10-09).
    public static PortfolioGroup Of(AssetKind kind) => kind switch
    {
        AssetKind.Stock or AssetKind.Unit => PortfolioGroup.Stocks,
        AssetKind.Fii => PortfolioGroup.Fiis,
        AssetKind.Etf => PortfolioGroup.Etfs,
        AssetKind.Bdr => PortfolioGroup.Bdrs,
        AssetKind.FiInfra or AssetKind.FiAgro or AssetKind.Fip or AssetKind.Fidc or AssetKind.OtherFund => PortfolioGroup.OtherFunds,
        _ => PortfolioGroup.Other,
    };
}

public static class PortfolioWeights
{
    // Peso de cada valor sobre a soma, em centésimos de ponto (10000 = 100%), fechando 100% pelo maior resto: cada um
    // leva a parte inteira, e a sobra vai, um centésimo por vez, para as maiores frações (empate: o que vem antes). Soma
    // zero: tudo zero.
    public static int[] BasisPoints(IReadOnlyList<long> values)
    {
        var total = values.Aggregate(Int128.Zero, (sum, v) => sum + v);
        var weights = new int[values.Count];
        if (total == 0)
            return weights;

        var remainders = new Int128[values.Count];
        var left = 10000;
        for (var i = 0; i < values.Count; i++)
        {
            var scaled = (Int128)values[i] * 10000;
            weights[i] = (int)(scaled / total);
            remainders[i] = scaled % total;
            left -= weights[i];
        }

        foreach (var i in Enumerable.Range(0, values.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(left))
            weights[i]++;
        return weights;
    }
}

// Um provento visto pela carteira: de qual ativo, quando caiu e quanto.
public sealed record PortfolioPayout(Guid AssetId, DateOnly Date, long AmountCents);

// Um ativo na carteira do ano: a posição na data, o peso no total e o que rendeu no ano (proventos e vendas).
public sealed record PortfolioAsset(
    Guid AssetId, PortfolioGroup Group, PositionTotals Totals, int WeightBasisPoints, long PayoutsCents, long RealizedCents);

// Um grupo: quanto custou o que se tem, o peso, quantos ativos com quantidade, e o que rendeu no ano.
public sealed record PortfolioSlice(
    PortfolioGroup Group, long InvestedCents, int WeightBasisPoints, int AssetCount, long PayoutsCents, long RealizedCents);

// A carteira em um ano (docs/investimentos.md, I.7e): no fim de 31/12, ou de hoje no ano corrente. Investido é o custo
// de aquisição do que tem quantidade na data. Proventos e resultado das vendas: os do ano, até a data. Entra na lista
// quem tem quantidade, ou teve provento ou venda no ano. Do maior custo para o menor (empate: pelo código; grupos: na
// ordem dos grupos).
public sealed record PortfolioSnapshot(
    int Year,
    DateOnly AsOf,
    long InvestedCents,
    long PayoutsCents,
    long RealizedCents,
    IReadOnlyList<PortfolioSlice> Groups,
    IReadOnlyList<PortfolioAsset> Assets)
{
    public const int FirstYear = 1900;

    public static Result<PortfolioSnapshot> Build(
        int year,
        DateOnly today,
        IReadOnlyList<AssetPosition> positions,
        IReadOnlyList<PortfolioPayout> payouts,
        Func<Guid, AssetKind> kindOf,
        Func<Guid, string> symbolOf)
    {
        if (year > today.Year)
            return new Error(ErrorType.Validation, $"Escolha um ano até {today.Year}.");
        if (year < FirstYear)
            return new Error(ErrorType.Validation, $"Escolha um ano a partir de {FirstYear}.");

        var start = new DateOnly(year, 1, 1);
        var asOf = year == today.Year ? today : new DateOnly(year, 12, 31);

        var paid = payouts.Where(p => p.Date >= start && p.Date <= asOf)
            .GroupBy(p => p.AssetId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.AmountCents));
        var held = positions.ToDictionary(p => p.AssetId);

        var rows = held.Keys.Union(paid.Keys)
            .Select(id =>
            {
                var position = held.GetValueOrDefault(id);
                var totals = position?.On(asOf) ?? PositionTotals.Empty;
                var realized = position?.RealizedBetween(start, asOf) ?? 0;
                return (Id: id, Totals: totals, Payouts: paid.GetValueOrDefault(id), Realized: realized);
            })
            .Where(r => r.Totals.Quantity > 0 || r.Payouts != 0 || r.Realized != 0)
            .OrderByDescending(r => r.Totals.Quantity > 0 ? r.Totals.CostBasisCents : 0)
            .ThenBy(r => symbolOf(r.Id), StringComparer.Ordinal)
            .ToList();

        long Invested((Guid Id, PositionTotals Totals, long Payouts, long Realized) r) => r.Totals.Quantity > 0 ? r.Totals.CostBasisCents : 0;

        var assetWeights = PortfolioWeights.BasisPoints(rows.Select(Invested).ToList());
        var assets = rows
            .Select((r, i) => new PortfolioAsset(r.Id, PortfolioGroups.Of(kindOf(r.Id)), r.Totals, assetWeights[i], r.Payouts, r.Realized))
            .ToList();

        var grouped = assets.GroupBy(a => a.Group)
            .Select(g => (Group: g.Key, Invested: g.Where(a => a.Totals.Quantity > 0).Sum(a => a.Totals.CostBasisCents),
                Count: g.Count(a => a.Totals.Quantity > 0), Payouts: g.Sum(a => a.PayoutsCents), Realized: g.Sum(a => a.RealizedCents)))
            .OrderByDescending(g => g.Invested)
            .ThenBy(g => g.Group)
            .ToList();
        var groupWeights = PortfolioWeights.BasisPoints(grouped.Select(g => g.Invested).ToList());
        var groups = grouped
            .Select((g, i) => new PortfolioSlice(g.Group, g.Invested, groupWeights[i], g.Count, g.Payouts, g.Realized))
            .ToList();

        return new PortfolioSnapshot(year, asOf, groups.Sum(g => g.InvestedCents), groups.Sum(g => g.PayoutsCents),
            groups.Sum(g => g.RealizedCents), groups, assets);
    }
}
