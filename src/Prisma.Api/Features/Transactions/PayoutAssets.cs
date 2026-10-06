using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Transactions;

// O código e o tipo do ativo de cada provento da lista, e se ele tem logo, numa consulta só (docs/investimentos.md, etapas 5b
// e 4): a linha mostra "Dividendo · BBAS3" com o logo.
public static class PayoutAssets
{
    public static async Task<IReadOnlyList<TransactionResponse>> Fill(
        AppDbContext db, IReadOnlyList<TransactionResponse> transactions, CancellationToken ct)
    {
        var ids = transactions.Where(t => t.AssetId is not null).Select(t => t.AssetId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return transactions;

        var assets = await db.Assets.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Symbol, a.Kind, HasLogo = db.AssetLogos.Any(l => l.AssetId == a.Id) })
            .ToDictionaryAsync(a => a.Id, ct);
        return transactions
            .Select(t => t.AssetId is { } assetId && assets.GetValueOrDefault(assetId) is { } asset
                ? t with { AssetSymbol = asset.Symbol, AssetHasLogo = asset.HasLogo, AssetKind = asset.Kind }
                : t)
            .ToList();
    }
}
