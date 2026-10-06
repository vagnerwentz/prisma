using Prisma.Domain.Investments;
using Prisma.Domain.Market;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Investments;

// Um provento com o que a tela mostra do ativo (docs/investimentos.md, etapa 5b). Id é o do lançamento: excluir
// e desfazer usam os endpoints de lançamento.
public sealed record PayoutResponse(
    Guid Id, Guid AccountId, Guid AssetId, string Symbol, string AssetName, AssetKind AssetKind,
    PayoutKind Kind, long AmountCents, DateOnly Date, bool AssetHasLogo)
{
    public static PayoutResponse From(Transaction payout, Asset asset, bool assetHasLogo) =>
        new(payout.Id, payout.AccountId, asset.Id, asset.Symbol, asset.DisplayName, asset.Kind,
            payout.PayoutKind!.Value, payout.AmountCents, payout.PurchaseDate, assetHasLogo);
}
