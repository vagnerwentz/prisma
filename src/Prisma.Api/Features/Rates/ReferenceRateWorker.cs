using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Domain;

namespace Prisma.Api.Features.Rates;

// Roda a sincronização do CDI nos horários marcados (ReferenceRateSchedule) e também ao subir, se o histórico estiver vazio ou
// atrasado. Uma falha nunca derruba a API: exceção que escapasse do ExecuteAsync pararia o host. Ela vai para o log, e o horário
// seguinte tenta de novo; o histórico não some, só fica um dia atrás (e o cálculo avisa "rendimento até <data>").
public sealed class ReferenceRateWorker(
    ReferenceRateSync sync, IClock clock, IOptions<RateSyncOptions> options, ILogger<ReferenceRateWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnce(onlyIfNeeded: true, stoppingToken);

            var times = options.Value.ParsedRunTimes();
            while (true)
            {
                var next = ReferenceRateSchedule.NextRun(clock.UtcNow, times);
                logger.ReferenceRateNextRun(next);
                var wait = next - clock.UtcNow;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, stoppingToken);
                await RunOnce(onlyIfNeeded: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está parando.
        }
    }

    private async Task RunOnce(bool onlyIfNeeded, CancellationToken ct)
    {
        try
        {
            if (onlyIfNeeded && !await sync.NeedsRunAsync(ct))
                return;
            await sync.RunAsync(ct);
        }
        // O Banco Central fora do ar é esperado de vez em quando: aviso, não erro.
        catch (MarketDataException ex)
        {
            logger.ReferenceRateSyncUnavailable(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.ReferenceRateSyncFailed(ex);
        }
    }
}
