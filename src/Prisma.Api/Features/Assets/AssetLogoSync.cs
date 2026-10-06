using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Assets;

public sealed record AssetLogoRunSummary(int Pending, int Saved, int Removed, int Failed, int Rejected);

// Baixa, limpa e guarda os logos dos ativos (docs/investimentos.md, etapa 4). Roda depois do catálogo: pega só o
// ativo com logo ainda não guardado, ou guardado de outro endereço. Ativo que perdeu o logo no fornecedor perde o
// guardado também (a tela volta ao código). Cada logo falha sozinho: o próximo dia tenta de novo, porque nada foi
// guardado para ele.
public sealed class AssetLogoSync(IServiceScopeFactory scopes, IClock clock, ILogger<AssetLogoSync> logger)
{
    // Downloads ao mesmo tempo: rápido na primeira vez (centenas de logos) sem martelar o fornecedor.
    public const int Parallelism = 4;

    public async Task<AssetLogoRunSummary> RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var downloader = scope.ServiceProvider.GetRequiredService<IAssetLogoDownloader>();

        var pending = await db.Assets.AsNoTracking()
            .Where(a => a.LogoUrl != null && !db.AssetLogos.Any(l => l.AssetId == a.Id && l.SourceUrl == a.LogoUrl))
            .Select(a => new { a.Id, a.Symbol, Url = a.LogoUrl! })
            .ToListAsync(ct);

        var downloaded = new System.Collections.Concurrent.ConcurrentBag<(Guid Id, string Symbol, string Url, string? Svg)>();
        var failed = 0;
        await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (asset, token) =>
            {
                try
                {
                    downloaded.Add((asset.Id, asset.Symbol, asset.Url, await downloader.DownloadAsync(asset.Url, token)));
                }
                catch (MarketDataException ex)
                {
                    Interlocked.Increment(ref failed);
                    logger.AssetLogoSkipped(asset.Symbol, ex.Message);
                }
            });

        var ids = downloaded.Select(d => d.Id).ToList();
        var existing = await db.AssetLogos.Where(l => ids.Contains(l.AssetId)).ToDictionaryAsync(l => l.AssetId, ct);
        var now = clock.UtcNow;
        int saved = 0, rejected = 0;
        foreach (var (id, symbol, url, raw) in downloaded)
        {
            var svg = SvgSanitizer.Clean(raw);
            if (svg is null)
            {
                rejected++;
                logger.AssetLogoSkipped(symbol, "not a clean svg");
                continue;
            }

            if (existing.TryGetValue(id, out var logo))
                logo.Replace(svg, url, now);
            else
                db.AssetLogos.Add(AssetLogo.Create(id, svg, url, now));
            saved++;
        }

        var removed = await db.AssetLogos
            .Where(l => db.Assets.Any(a => a.Id == l.AssetId && a.LogoUrl == null))
            .ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        var summary = new AssetLogoRunSummary(pending.Count, saved, removed, failed, rejected);
        logger.AssetLogosSynced(summary.Pending, summary.Saved, summary.Removed, summary.Failed, summary.Rejected);
        return summary;
    }
}
