using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain;

namespace Prisma.Api.Features.Assets;

// Roda a sincronização do catálogo de ativos e, em seguida, a dos logos, uma vez por dia (AssetSyncSchedule). Com o
// catálogo vazio ou sem nenhum logo (a primeira subida), roda logo ao iniciar, sem esperar a madrugada. Os logos rodam
// mesmo se a lista falhar: o que estava pendente pode ser baixado.
//
// Uma falha nunca derruba a API: exceção que escapasse do ExecuteAsync pararia o host. Ela vai para o log,
// e o dia seguinte tenta de novo; perder um dia não muda nada, porque a lista muda pouco.
public sealed class AssetListWorker(
    AssetListSync sync, AssetLogoSync logos, IClock clock, IOptions<AssetSyncOptions> options, ILogger<AssetListWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnce(onlyIfEmpty: true, stoppingToken);

            while (true)
            {
                var next = AssetSyncSchedule.NextRun(clock.UtcNow, options.Value.HourOfDay);
                logger.AssetListNextRun(next);
                var wait = next - clock.UtcNow;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, stoppingToken);
                await RunOnce(onlyIfEmpty: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está parando.
        }
    }

    private async Task RunOnce(bool onlyIfEmpty, CancellationToken ct)
    {
        try
        {
            if (onlyIfEmpty && !await sync.NeedsFirstRunAsync(ct))
                return;
            await sync.RunAsync(ct);
        }
        // O fornecedor fora do ar é esperado de vez em quando: aviso, não erro.
        catch (MarketDataException ex)
        {
            logger.AssetListSyncUnavailable(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.AssetListSyncFailed(ex);
        }

        try
        {
            await logos.RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.AssetLogoSyncFailed(ex);
        }
    }
}
