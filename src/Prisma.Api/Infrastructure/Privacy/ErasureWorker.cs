using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Privacy;

// Roda a limpeza ao subir a API e depois a cada ErasureRunner.Interval (etapa A.3). Ao subir conta: depois de restaurar
// um backup, a conta apagada que voltou sai antes de alguém usar o app.
//
// Uma falha nunca derruba a API: ela vai para o log, e a próxima volta tenta de novo.
public sealed class ErasureWorker(ErasureRunner runner, ILogger<ErasureWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ErasureWorkerStarted(ErasureRunner.Interval.TotalMinutes);
        using var timer = new PeriodicTimer(ErasureRunner.Interval);
        try
        {
            do
            {
                await RunOnce(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está parando.
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await runner.RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.ErasureRunFailed(ex);
        }
    }
}
