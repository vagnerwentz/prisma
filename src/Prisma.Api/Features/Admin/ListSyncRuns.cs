using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Admin;

// As últimas execuções da sincronização de uma série, da mais nova para a mais antiga (docs/admin-mercado.md, 4).
public static class ListSyncRuns
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<MarketSyncRunResponse>>> Execute(string seriesCode, int? limit, CancellationToken ct)
        {
            if (MarketSeriesCatalog.Find(seriesCode) is not { } series)
                return SyncMarketData.UnknownSeries;

            var runs = await db.MarketSyncRuns.AsNoTracking()
                .Where(r => r.Series == series.Series)
                .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit))
                .ToListAsync(ct);
            return runs.Select(MarketSyncRunResponse.Of).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{series}/runs", async (string series, int? limit, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(series, limit, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<MarketSyncRunResponse>>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);
}
