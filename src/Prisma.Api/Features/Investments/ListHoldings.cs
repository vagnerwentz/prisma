using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Investments;

// Os ativos da carteira, em ordem de código (docs/investimentos.md, seção 8, etapa 5a).
public static class ListHoldings
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<HoldingResponse>> Execute(CancellationToken ct)
        {
            var rows = await db.Holdings
                .AsNoTracking()
                .Join(db.Assets.AsNoTracking(), h => h.AssetId, a => a.Id, (holding, asset) => new { holding, asset })
                .OrderBy(x => x.asset.Symbol)
                .Select(x => new { x.holding, x.asset, hasLogo = db.AssetLogos.Any(l => l.AssetId == x.asset.Id) })
                .ToListAsync(ct);
            return rows.Select(x => HoldingResponse.From(x.holding, x.asset, x.hasLogo)).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<HoldingResponse>>(200);
}
