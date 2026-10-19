using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.Rates;

// Roda a sincronização do CDI nos horários marcados (ReferenceRateSchedule) e também ao subir, se o histórico estiver vazio ou
// atrasado. Uma falha nunca derruba a API: exceção que escapasse do ExecuteAsync pararia o host. Ela vai para o log, e o horário
// seguinte tenta de novo; o histórico não some, só fica um dia atrás (e o cálculo avisa "rendimento até <data>"). Cada execução fica
// gravada em `market_sync_runs` (ReferenceRateSync.RunRecordedAsync), para a tela de admin mostrar; o disparo manual dela divide o
// mesmo lock e não mexe nos horários.
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
            // O Banco Central fora do ar e o erro da própria execução viram o desfecho gravado (e o log), não exceção.
            await sync.RunRecordedAsync(onlyIfNeeded ? MarketSyncKind.Startup : MarketSyncKind.Scheduled, null, null, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.ReferenceRateSyncFailed(ex);
        }
    }
}
