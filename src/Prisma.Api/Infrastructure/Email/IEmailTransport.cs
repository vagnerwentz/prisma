namespace Prisma.Api.Infrastructure.Email;

// O fornecedor de envio (docs/email.md, seção 2). O resto do código não sabe qual é: trocar o Resend por outro é
// escrever outra implementação e mudar Email:Transport. Não lança exceção por falha do fornecedor: devolve Retry
// ou Reject.
public interface IEmailTransport
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

// Monta o conteúdo de um tipo de e-mail na hora do envio, com o link e o token novos. Nulo: o motivo deixou de
// existir (quem pediu a confirmação já confirmou) e nada é enviado.
public interface IEmailComposer
{
    EmailKind Kind { get; }

    Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct);
}
