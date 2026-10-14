namespace Prisma.Api.Infrastructure.Email;

public enum EmailStatus
{
    Pending,
    Sent,
    // O motivo deixou de existir antes do envio (ex.: confirmação pedida de novo para quem já confirmou).
    Skipped,
    Failed,
}

// Um e-mail a enviar (docs/email.md, seção 3). Nasce na mesma gravação que o motivo dele e não guarda o
// link nem o token: o conteúdo é montado na hora do envio (IEmailComposer). Tabela sem dono
// (AppDbContext.GlobalTables): só a API escreve, só a tarefa lê, e o UserId é referência.
public sealed class OutboxEmail
{
    public const int AddressMaxLength = 256;
    public const int ErrorMaxLength = 200;
    public const int ProviderIdMaxLength = 100;

    private OutboxEmail()
    {
    }

    public Guid Id { get; private set; }

    public EmailKind Kind { get; private set; }

    public Guid UserId { get; private set; }

    public string ToAddress { get; private set; } = null!;

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime NextAttemptAt { get; private set; }

    // Código do motivo (http_503, timeout), nunca o endereço nem a mensagem do fornecedor, que pode citá-lo.
    public string? LastError { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    public static OutboxEmail Create(EmailKind kind, Guid userId, string toAddress, DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        UserId = userId,
        ToAddress = toAddress.Trim(),
        Status = EmailStatus.Pending,
        NextAttemptAt = utcNow,
        CreatedAt = utcNow,
    };

    public void MarkSent(string? providerMessageId, DateTime utcNow)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentAt = utcNow;
        ProviderMessageId = providerMessageId;
        LastError = null;
    }

    public void MarkSkipped()
    {
        Status = EmailStatus.Skipped;
    }

    // Falha passageira: tenta de novo mais tarde, ou desiste quando o prazo acabou (EmailRetryPolicy).
    public void MarkRetry(string reason, DateTime utcNow)
    {
        Attempts++;
        LastError = Truncate(reason);
        var next = EmailRetryPolicy.NextAttemptAt(CreatedAt, Attempts, utcNow);
        if (next is null)
            Status = EmailStatus.Failed;
        else
            NextAttemptAt = next.Value;
    }

    public void MarkFailed(string reason)
    {
        Attempts++;
        Status = EmailStatus.Failed;
        LastError = Truncate(reason);
    }

    private static string Truncate(string reason) =>
        reason.Length <= ErrorMaxLength ? reason : reason[..ErrorMaxLength];
}
