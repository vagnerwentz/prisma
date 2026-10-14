namespace Prisma.Api.Infrastructure.Email;

// O que um fornecedor precisa para enviar. Endereços no formato "Nome <endereco@dominio>" ou só o endereço.
public sealed record EmailMessage(
    string From,
    string ReplyTo,
    string To,
    string Subject,
    string Html,
    string Text);

// O conteúdo de um e-mail, montado na hora do envio pelo IEmailComposer do tipo.
public sealed record EmailContent(string Subject, string Html, string Text);

public enum EmailSendStatus
{
    Sent,
    // Falha passageira (rede, tempo esgotado, limite do fornecedor, fornecedor fora do ar, chave errada): tentar de novo.
    Retry,
    // Repetir não conserta (endereço recusado, pedido inválido).
    Reject,
}

// Reason é um código curto para o log e a fila (http_503, timeout), nunca a mensagem do fornecedor.
public sealed record EmailSendResult(EmailSendStatus Status, string? ProviderMessageId, string? Reason)
{
    public static EmailSendResult Sent(string? providerMessageId) => new(EmailSendStatus.Sent, providerMessageId, null);

    public static EmailSendResult Retry(string reason) => new(EmailSendStatus.Retry, null, reason);

    public static EmailSendResult Reject(string reason) => new(EmailSendStatus.Reject, null, reason);
}
