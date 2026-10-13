using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Investments;

// Os avisos de ativo faltando, do mais novo ao mais velho: os que esperam e os que chegaram (docs/investimentos.md,
// I.7c3). O que chegou e já tem nota sai da lista.
public static class ListAssetRequests
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<AssetRequestResponse>> Execute(CancellationToken ct)
        {
            var requests = await db.AssetRequests.AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .ToListAsync(ct);
            return await AssetRequestBook.Describe(db, requests, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<AssetRequestResponse>>(200);
}
