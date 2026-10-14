using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Email;

// Olha a fila a cada poucos segundos (Email:Outbox:PollSeconds) e envia o que venceu; lote cheio, olha de novo na hora.
// Uma vez por dia, ao subir e depois a cada 24 h, apaga o que já saiu há mais de 30 dias.
//
// Uma falha nunca derruba a API: exceção que escapasse do ExecuteAsync pararia o host. Ela vai para o log, e a próxima
// volta tenta de novo; o e-mail continua na fila.
public sealed class EmailOutboxWorker(
    EmailOutboxProcessor processor, IOptions<EmailOptions> options, IClock clock, ILogger<EmailOutboxWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromDays(1);

    private DateTime? _lastCleanup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var outbox = options.Value.Outbox;
        logger.EmailOutboxWorkerStarted(outbox.PollSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(outbox.PollSeconds));
        try
        {
            do
            {
                await RunOnce(outbox.BatchSize, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A API está parando.
        }
    }

    private async Task RunOnce(int batchSize, CancellationToken ct)
    {
        try
        {
            EmailRunSummary summary;
            do
            {
                summary = await processor.RunOnceAsync(ct);
            }
            while (summary.Claimed == batchSize);

            if (_lastCleanup is null || clock.UtcNow - _lastCleanup >= CleanupEvery)
            {
                await processor.CleanupAsync(ct);
                _lastCleanup = clock.UtcNow;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.EmailOutboxRunFailed(ex);
        }
    }
}
