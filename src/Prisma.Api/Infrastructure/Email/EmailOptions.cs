using System.Net.Mail;

namespace Prisma.Api.Infrastructure.Email;

public enum EmailTransportKind
{
    Resend,
    // Desenvolvimento: cada e-mail vira um .html numa pasta local, para abrir no navegador.
    Folder,
}

// Configuração do envio (seção "Email"; docs/email.md, seção 2). A chave do Resend vem dos User Secrets em
// desenvolvimento e de Email__Resend__ApiKey em produção; nunca do repositório.
public sealed class EmailOptions
{
    public const string Section = "Email";

    public EmailTransportKind Transport { get; init; } = EmailTransportKind.Resend;

    public string From { get; init; } = "";

    public string ReplyTo { get; init; } = "";

    public string Folder { get; init; } = ".dev-emails";

    public ResendOptions Resend { get; init; } = new();

    public OutboxOptions Outbox { get; init; } = new();

    public CampaignOptions Campaigns { get; init; } = new();

    // Fora de desenvolvimento só o Resend, e com a chave: melhor a API não subir do que aceitar cadastros cujo e-mail
    // nunca sai.
    public bool IsValid(bool isDevelopment) =>
        IsAddress(From) && IsAddress(ReplyTo) &&
        Resend.IsValid() && Outbox.IsValid() && Campaigns.IsValid() &&
        (Transport == EmailTransportKind.Resend
            ? !string.IsNullOrWhiteSpace(Resend.ApiKey)
            : isDevelopment && !string.IsNullOrWhiteSpace(Folder));

    private static bool IsAddress(string value) => MailAddress.TryCreate(value, out _);
}

public sealed class ResendOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.resend.com/");

    public string? ApiKey { get; init; }

    // Um envio lento não prende a fila: o tempo esgotado conta como falha passageira.
    public int TimeoutSeconds { get; init; } = 15;

    public bool IsValid() => BaseUrl.IsAbsoluteUri && TimeoutSeconds is > 0 and <= 60;
}

public sealed class OutboxOptions
{
    public const string EnabledKey = "Email:Outbox:Enabled";

    // De quanto em quanto tempo a tarefa olha a fila. Uma consulta pequena, com índice; o e-mail sai em segundos.
    public int PollSeconds { get; init; } = 5;

    public int BatchSize { get; init; } = 20;

    public bool IsValid() => PollSeconds is > 0 and <= 300 && BatchSize is > 0 and <= 500;
}

// Disparos do admin (docs/admin-email.md, seção 4). O teto existe por causa da cota do fornecedor (o plano gratuito do Resend
// tem 100 por dia): um disparo não pode atrasar o e-mail de senha de ninguém.
public sealed class CampaignOptions
{
    public const int DefaultMaxRecipients = 50;

    // Quantas pessoas no máximo um disparo alcança. Acima disso o envio é recusado, não cortado.
    public int MaxRecipients { get; init; } = DefaultMaxRecipients;

    // Intervalo entre um e-mail e o seguinte do mesmo disparo na fila.
    public int StaggerSeconds { get; init; } = 5;

    public bool IsValid() => MaxRecipients is > 0 and <= 500 && StaggerSeconds is >= 0 and <= 60;
}
