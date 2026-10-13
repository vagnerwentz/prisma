using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Assets;

// Leva a curadoria dos eventos da empresa para o banco (docs/investimentos.md, I.7c1): primeiro os ativos que saíram da
// bolsa antes do catálogo (entram inativos), depois os eventos (CorporateEventReconciliation decide), numa transação só.
// Roda ao subir a API e depois de cada sincronização do catálogo: o destino de uma conversão pode chegar depois.
//
// Evento com código fora do catálogo fica de fora, com aviso, e o que já estava gravado dele fica como está. Arquivo
// inválido não muda nada.
public sealed class CorporateEventSync(IServiceScopeFactory scopes, IClock clock, ILogger<CorporateEventSync> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<ICorporateEventSource>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = source.Load();
        var now = clock.UtcNow;

        var catalog = await db.Assets.ToDictionaryAsync(a => a.Symbol, StringComparer.Ordinal, ct);
        var assetsAdded = 0;
        foreach (var entry in file.HistoricalAssets)
        {
            if (catalog.ContainsKey(entry.Symbol))
                continue;
            if (!ListedAsset.TryCreate(entry.Symbol, entry.Name, entry.Name, entry.Kind, out var listed))
            {
                logger.CorporateEventsRefused($"historical asset {entry.Symbol} is invalid");
                return;
            }

            var asset = Asset.FromHistory(listed, entry.LastTradingDay, now);
            db.Assets.Add(asset);
            catalog[asset.Symbol] = asset;
            assetsAdded++;
        }

        var definitions = new List<CorporateEventDefinition>();
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in file.Events)
        {
            var missing = new[] { e.Symbol, e.Target }.FirstOrDefault(s => s is not null && !catalog.ContainsKey(s));
            if (missing is not null)
            {
                logger.CorporateEventSkipped(e.Key, missing);
                skipped.Add(e.Key);
                continue;
            }

            if (!TryUnitValue(e.TargetUnitPrice, out var price) || !TryUnitValue(e.CashPerUnit, out var cash))
            {
                logger.CorporateEventsRefused($"event {e.Key} has an invalid unit value");
                return;
            }

            definitions.Add(new CorporateEventDefinition(
                e.Key, e.Kind, catalog[e.Symbol].Id, e.Target is null ? null : catalog[e.Target].Id, e.Date, e.From, e.To, e.Source,
                price, cash));
        }

        var current = await db.CorporateEvents.Where(e => !skipped.Contains(e.Key)).ToListAsync(ct);
        var changes = CorporateEventReconciliation.Apply(current, definitions, now);
        if (!changes.IsSuccess)
        {
            logger.CorporateEventsRefused(changes.Error.Message);
            return;
        }

        db.CorporateEvents.AddRange(changes.Value.Added);
        db.CorporateEvents.RemoveRange(changes.Value.Removed);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        // Ao subir, esta execução e a que segue a sincronização do catálogo podem correr juntas: quem perde no índice
        // único encontra o trabalho feito pela outra.
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AssetConfiguration.SymbolIndex) || ex.IsUniqueViolation(CorporateEventConfiguration.KeyIndex))
        {
            return;
        }
        logger.CorporateEventsSynced(assetsAdded, changes.Value.Added.Count, changes.Value.Updated, changes.Value.Removed.Count);
    }

    // Valor por cota do arquivo (I.7c4). Ausente: nulo. Presente e fora do formato: o arquivo inteiro é recusado, como
    // qualquer evento inválido.
    private static bool TryUnitValue(string? text, out long? value)
    {
        value = null;
        if (text is null)
            return true;
        if (!CorporateEvent.TryParseUnitValue(text, out var parsed))
            return false;
        value = parsed;
        return true;
    }
}

// Ao subir a API: um arquivo novo chega com o deploy. Falha vai para o log e não derruba a API.
public sealed class CorporateEventStartup(CorporateEventSync sync, ILogger<CorporateEventStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await sync.RunAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.CorporateEventSyncFailed(ex);
        }
    }
}
