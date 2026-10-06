using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Market.Brapi;

// Tradução do assetType/subType da brapi para o AssetKind. Valores aceitos, segundo a documentação:
// assetType stock, fund, bdr; subType stock, unit, fii, etf, fi-infra, fi-agro, fip, fidc, bdr.
internal static class BrapiAssetKinds
{
    public static AssetKind From(string? assetType, string? subType) =>
        (Normalize(assetType), Normalize(subType)) switch
        {
            ("stock", "stock" or null) => AssetKind.Stock,
            ("stock", "unit") => AssetKind.Unit,
            ("fund", "fii") => AssetKind.Fii,
            ("fund", "etf") => AssetKind.Etf,
            ("fund", "fi-infra") => AssetKind.FiInfra,
            ("fund", "fi-agro") => AssetKind.FiAgro,
            ("fund", "fip") => AssetKind.Fip,
            ("fund", "fidc") => AssetKind.Fidc,
            // Fundo listado sem subtipo: existe na lista da brapi.
            ("fund", null) => AssetKind.OtherFund,
            ("bdr", "bdr" or null) => AssetKind.Bdr,
            _ => AssetKind.Unknown,
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
