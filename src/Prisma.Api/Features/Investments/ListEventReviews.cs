using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Investments;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// O que pede conferência (docs/investimentos.md, I.7c2): os eventos que mexeram na quantidade da pessoa, sem conferência
// da versão atual, do mais antigo ao mais novo. QuantityAfter: no desdobramento, a do próprio ativo; na conversão e na
// liquidação, as cotas do destino. TargetUnitPriceCents: o preço de referência da liquidação, em centavos para a tela.
public static class ListEventReviews
{
    public sealed record EventReviewItem(
        Guid EventId, CorporateEventKind Kind, DateOnly Date, Guid AssetId, string Symbol, AssetKind AssetKind, bool HasLogo,
        string? TargetSymbol, int FromQuantity, int ToQuantity, long QuantityBefore, long QuantityAfter, long? TargetUnitPriceCents);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<IReadOnlyList<EventReviewItem>>> Execute(CancellationToken ct)
        {
            var effects = await EventReviewBook.Effects(db, clock.Today, ct);
            if (!effects.IsSuccess)
                return effects.Error;

            var reviews = await db.EventReviews.AsNoTracking().ToListAsync(ct);
            var pending = EventEffects.Pending(effects.Value, reviews);
            var assets = await TradeBook.Assets(db,
                pending.SelectMany(p => p.Event.TargetAssetId is { } target ? [p.AssetId, target] : new[] { p.AssetId }), ct);
            return pending.Select(p =>
            {
                var e = p.Event;
                var asset = assets[p.AssetId];
                return new EventReviewItem(e.Id, e.Kind, e.Date, p.AssetId, asset.Symbol, asset.Kind, asset.HasLogo,
                    e.TargetAssetId is { } t ? assets[t].Symbol : null, e.FromQuantity, e.ToQuantity, p.QuantityBefore, p.QuantityAfter,
                    e.TargetUnitPrice is { } price ? (price + CorporateEvent.UnitValueScale / 200) / (CorporateEvent.UnitValueScale / 100) : null);
            }).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<EventReviewItem>>(200)
            .ProducesProblem(409);
}
