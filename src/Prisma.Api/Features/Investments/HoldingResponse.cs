using Prisma.Domain.Investments;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// Um ativo da carteira, com o que a tela mostra do catálogo. Name é o nome mostrado (Asset.DisplayName).
public sealed record HoldingResponse(
    Guid Id, Guid AssetId, string Symbol, string Name, AssetKind Kind, bool IsActive, DateTime AddedAt, bool HasLogo)
{
    public static HoldingResponse From(Holding holding, Asset asset, bool hasLogo) =>
        new(holding.Id, asset.Id, asset.Symbol, asset.DisplayName, asset.Kind, asset.IsActive, holding.CreatedAt, hasLogo);
}
