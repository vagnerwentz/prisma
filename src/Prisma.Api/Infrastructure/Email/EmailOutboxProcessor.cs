using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Email;

public sealed record EmailRunSummary(int Claimed, int Sent, int Retried, int Failed, int Skipped);

// Envia o que está na fila (docs/email.md, seção 3). Cada execução reserva um lote e envia um a um; cada e-mail grava
// o próprio resultado, e o que falha não trava os outros.
//
// A reserva empurra o NextAttemptAt para daqui a Lease, numa só instrução com FOR UPDATE SKIP LOCKED: duas execuções
// ao mesmo tempo (outra instância, um teste) nunca pegam o mesmo e-mail, e nenhuma transação fica aberta durante o
// envio. Se a API cair no meio, a reserva vence e o e-mail volta para a fila.
public sealed class EmailOutboxProcessor(
    IServiceScopeFactory scopes, IOptions<EmailOptions> options, IClock clock, ILogger<EmailOutboxProcessor> logger)
{
    // Maior que o tempo de um envio (Email:Resend:TimeoutSeconds, até 60 s) com folga.
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    // A fila não é histórico: o endereço é dado pessoal (regra 7).
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task<EmailRunSummary> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var composers = scope.ServiceProvider.GetServices<IEmailComposer>().ToDictionary(c => c.Kind);

        var ids = await ClaimAsync(db, ct);
        var summary = new EmailRunSummary(ids.Count, 0, 0, 0, 0);
        foreach (var id in ids)
        {
            var email = await db.OutboxEmails.SingleAsync(e => e.Id == id, ct);
            var status = await ProcessAsync(email, transport, composers, ct);
            await db.SaveChangesAsync(ct);
            summary = status switch
            {
                EmailStatus.Sent => summary with { Sent = summary.Sent + 1 },
                EmailStatus.Failed => summary with { Failed = summary.Failed + 1 },
                EmailStatus.Skipped => summary with { Skipped = summary.Skipped + 1 },
                _ => summary with { Retried = summary.Retried + 1 },
            };
        }

        return summary;
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = clock.UtcNow - Retention;

        var removed = await db.OutboxEmails
            .Where(e => e.Status != EmailStatus.Pending && e.CreatedAt < before)
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
            logger.EmailOutboxCleaned(removed);
        return removed;
    }

    private async Task<List<Guid>> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var leaseUntil = now + Lease;
        var pending = nameof(EmailStatus.Pending);
        var batch = options.Value.Outbox.BatchSize;

        return await db.Database.SqlQuery<Guid>($"""
            UPDATE outbox_emails SET next_attempt_at = {leaseUntil}
            WHERE id IN (
                SELECT id FROM outbox_emails
                WHERE status = {pending} AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }

    private async Task<EmailStatus> ProcessAsync(
        OutboxEmail email, IEmailTransport transport, IReadOnlyDictionary<EmailKind, IEmailComposer> composers, CancellationToken ct)
    {
        if (!composers.TryGetValue(email.Kind, out var composer))
        {
            email.MarkFailed("no_composer");
            logger.EmailFailed(email.Id, email.Kind, email.Attempts, "no_composer");
            return email.Status;
        }

        EmailSendResult result;
        try
        {
            var content = await composer.ComposeAsync(email, ct);
            if (content is null)
            {
                email.MarkSkipped();
                logger.EmailSkipped(email.Id, email.Kind);
                return email.Status;
            }

            var settings = options.Value;
            result = await transport.SendAsync(
                new EmailMessage(settings.From, settings.ReplyTo, email.ToAddress, content.Subject, content.Html, content.Text), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Erro nosso ao montar o e-mail (banco, código): tenta de novo como uma falha passageira.
            logger.EmailProcessingFailed(email.Id, ex);
            result = EmailSendResult.Retry("exception");
        }

        var now = clock.UtcNow;
        switch (result.Status)
        {
            case EmailSendStatus.Sent:
                email.MarkSent(result.ProviderMessageId, now);
                logger.EmailSent(email.Id, email.Kind, email.UserId, email.Attempts);
                break;
            case EmailSendStatus.Retry:
                email.MarkRetry(result.Reason ?? "unknown", now);
                if (email.Status == EmailStatus.Failed)
                    logger.EmailFailed(email.Id, email.Kind, email.Attempts, email.LastError);
                else
                    logger.EmailRetryScheduled(email.Id, email.Kind, email.LastError, email.NextAttemptAt);
                break;
            default:
                email.MarkFailed(result.Reason ?? "rejected");
                logger.EmailFailed(email.Id, email.Kind, email.Attempts, email.LastError);
                break;
        }

        return email.Status;
    }
}
