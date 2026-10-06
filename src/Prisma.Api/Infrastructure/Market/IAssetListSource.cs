using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Market;

// Fonte da lista de ativos da bolsa (docs/investimentos.md, seção 2). Hoje é a brapi; trocar de fornecedor
// é escrever outra implementação, sem mexer em quem consome.
public interface IAssetListSource
{
    // A lista inteira ou nada: se uma página falha, lança MarketDataException e não devolve lista pela
    // metade. Quem sincroniza decide o que fazer com a falha (tentar no dia seguinte).
    Task<AssetList> FetchAllAsync(CancellationToken ct);
}

// Skipped: itens que vieram sem código válido, sem nome ou repetidos.
public sealed record AssetList(IReadOnlyList<ListedAsset> Assets, int Pages, int Skipped);
