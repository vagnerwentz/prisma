using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// As notas da pessoa e a carteira calculada delas (docs/investimentos.md, "I.7b1 API"). A posição nunca é gravada:
// toda gravação confere a carteira inteira com a mudança, e toda leitura a calcula de novo.
internal static class TradeBook
{
    public sealed record AssetInfo(string Symbol, string Name, AssetKind Kind, bool HasLogo, bool IsActive);

    // Em ordem de data; no mesmo dia, na ordem em que foram lançadas. Sem acompanhamento: quem grava acompanha só a
    // nota que muda (com 5.000 operações, acompanhar todas levava a gravação de 56 para 226 ms).
    public static Task<List<TradeNote>> Notes(AppDbContext db, CancellationToken ct) =>
        db.TradeNotes.AsNoTracking().OrderBy(n => n.Date).ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync(ct);

    public static Task<Dictionary<Guid, AssetInfo>> Assets(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        return db.Assets.AsNoTracking()
            .Where(a => wanted.Contains(a.Id))
            .Select(a => new { Asset = a, HasLogo = db.AssetLogos.Any(l => l.AssetId == a.Id) })
            .ToDictionaryAsync(x => x.Asset.Id, x => new AssetInfo(x.Asset.Symbol, x.Asset.DisplayName, x.Asset.Kind, x.HasLogo, x.Asset.IsActive), ct);
    }

    // Com os eventos da empresa (I.7c1): desdobramento, grupamento e conversão entram na linha do tempo.
    public static async Task<Result<IReadOnlyList<AssetPosition>>> Positions(
        AppDbContext db, IReadOnlyList<TradeNote> notes, CancellationToken ct)
    {
        var events = await db.CorporateEvents.AsNoTracking().ToListAsync(ct);
        var ids = notes.SelectMany(n => n.Trades).Select(t => t.AssetId)
            .Concat(events.SelectMany(e => e.TargetAssetId is { } target ? [e.AssetId, target] : new[] { e.AssetId }));
        var assets = await Assets(db, ids, ct);
        return PositionCalculator.Build(notes, events, id => assets[id].Symbol);
    }
}
