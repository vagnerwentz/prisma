using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Features.Recurrences;

// Roda o gerador ao subir a API e depois de hora em hora (docs/fase-2.md, 2.14, A4). A hora é folga, não
// regra: o gerador é idempotente e alcança o atraso, então rodar a mais ou a menos não muda o resultado.
//
// Uma falha nunca derruba a API: exceção que escapasse do ExecuteAsync pararia o host (padrão do .NET).
// Ela vai para o log, e a próxima hora tenta de novo.
public sealed class RecurrenceWorker(RecurrenceRunner runner, ILogger<RecurrenceWorker> logger, TimeSpan interval)
    : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    public RecurrenceWorker(RecurrenceRunner runner, ILogger<RecurrenceWorker> logger)
        : this(runner, logger, DefaultInterval)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.RecurrenceWorkerStarted(interval.TotalMinutes);
        using var timer = new PeriodicTimer(interval);
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
            logger.RecurrenceRunFailed(ex);
        }
    }
}
