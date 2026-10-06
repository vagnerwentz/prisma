using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Assets;

// Sincroniza o catálogo de ativos com a lista do fornecedor (docs/investimentos.md, seção 4). Busca a lista
// inteira, deixa o domínio decidir o que entra, muda ou fica inativo (AssetListReconciliation), e grava
// tudo numa transação só. Lista recusada pela trava de sanidade não grava nada.
//
// Falha do fornecedor (MarketDataException) sobe para quem chamou: o catálogo fica como estava, e a próxima
// execução tenta de novo.
//
// A fonte e o banco saem de um escopo novo a cada execução: o HttpClient tipado não pode viver para sempre
// num singleton (o IHttpClientFactory renova as conexões).
public sealed class AssetListSync(IServiceScopeFactory scopes, IClock clock, ILogger<AssetListSync> logger)
{
    public async Task<AssetListChanges> RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IAssetListSource>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var list = await source.FetchAllAsync(ct);
        var catalog = await db.Assets.ToListAsync(ct);
        var changes = AssetListReconciliation.Apply(catalog, list.Assets, clock.UtcNow);

        if (changes.IsRefused)
        {
            logger.AssetListSyncRefused(changes.ActiveBefore, changes.Listed);
            return changes;
        }

        db.Assets.AddRange(changes.Added);
        await db.SaveChangesAsync(ct);

        logger.AssetListSynced(changes.Listed, changes.Added.Count, changes.Updated, changes.Reactivated, changes.Deactivated);
        return changes;
    }

    // Primeira subida (catálogo vazio) ou primeira com logos (etapa 4, nenhum guardado): roda logo, sem esperar a
    // madrugada.
    public async Task<bool> NeedsFirstRunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return !await db.Assets.AnyAsync(ct) || !await db.AssetLogos.AnyAsync(ct);
    }
}
