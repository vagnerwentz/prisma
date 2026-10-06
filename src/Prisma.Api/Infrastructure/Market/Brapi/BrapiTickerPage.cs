namespace Prisma.Api.Infrastructure.Market.Brapi;

// Formato de GET /api/v2/tickers (https://brapi.dev/docs/tickers), só com o que lemos (o logo, desde a etapa 4):
// cotação, setor, bolsa, moeda, índices e facetas ficam de fora. Não sai desta pasta: o resto do app vê ListedAsset.
//
// Página e paginação são obrigatórias (sem elas a resposta é inválida); os campos de cada item são
// opcionais aqui e conferidos um a um, para um item ruim não derrubar a lista inteira.
internal sealed record BrapiTickerPage(IReadOnlyList<BrapiTicker> Results, BrapiPagination Pagination);

internal sealed record BrapiTicker(
    string? Symbol,
    string? Name,
    string? LongName,
    string? AssetType,
    string? SubType,
    string? LogoUrl = null);

internal sealed record BrapiPagination(int Page, bool HasNextPage);
