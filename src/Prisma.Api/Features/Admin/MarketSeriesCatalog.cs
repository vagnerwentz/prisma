using Prisma.Api.Infrastructure.Market.Bcb;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.Admin;

// As séries de mercado que a tela de admin sabe sincronizar (docs/admin-mercado.md, M6). O código vai na rota; só o que está nesta
// lista fechada chega à sincronização: nada de valor livre indo para o Banco Central. O IPCA entra aqui quando existir (R.18).
public sealed record MarketSeries(string Code, string Name, string Source, ReferenceRateSeries Series);

public static class MarketSeriesCatalog
{
    public static readonly IReadOnlyList<MarketSeries> All =
    [
        new("cdi", "CDI diário", BcbSgsRateSource.Provider, ReferenceRateSeries.Cdi),
    ];

    public static MarketSeries? Find(string code) =>
        All.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.Ordinal));

    public static string CodeOf(ReferenceRateSeries series) => All.Single(s => s.Series == series).Code;
}
