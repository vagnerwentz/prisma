using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Api.Features.Auth;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Domain;

namespace Prisma.Api.Features.Admin;

// Os textos dos e-mails de disparo do admin (docs/admin-email.md, seção 4). O layout é o EmailLayout; o texto aqui serve ao envio de
// verdade, à prévia e ao teste: o que o dono vê é o que a pessoa recebe, só com dados de exemplo.
public static class CampaignEmails
{
    public const string ExampleAddress = "pessoa@exemplo.com.br";

    private static readonly Guid ExampleUserId = Guid.Parse("0199e3a2-6f1c-7d3e-9a41-5b8c2d7e4f10");

    // O mesmo texto da confirmação do cadastro, com a frase de quem já tinha começado e o prazo do link novo.
    public static EmailTemplate ConfirmReminder(string address, Uri link, DateTime expiresAtUtc) => new(
        Subject: "Falta só confirmar seu e-mail no Prisma Money",
        Preheader: "Sua conta está criada. Falta um toque para começar a usar.",
        Eyebrow: "Confirmação de e-mail",
        Title: "Sua conta está esperando você",
        Paragraphs:
        [
            $"Você criou uma conta no Prisma Money com o e-mail **{address}**, mas ainda não confirmou o endereço. Sem isso, não dá para entrar.",
            "Toque no botão e digite a senha que você criou no cadastro. Você já entra no app em seguida.",
        ],
        Button: new EmailButton("Confirmar e-mail", link),
        Details:
        [
            new EmailDetail("Conta", address),
            new EmailDetail("Link válido até", AuthEmails.SaoPaulo(expiresAtUtc)),
        ],
        Note: "Não foi você? É só ignorar este e-mail. Sem a confirmação, a conta não é ativada.");

    public static EmailTemplate MissYou(string address, Uri signInLink) => new(
        Subject: "Sentimos sua falta no Prisma Money",
        Preheader: "Suas contas e lançamentos continuam guardados.",
        Eyebrow: "Faz um tempo",
        Title: "Sentimos sua falta",
        Paragraphs:
        [
            $"Faz um tempo que a conta **{address}** não aparece por aqui.",
            "Tudo o que você registrou continua guardado do jeito que ficou. Se quiser retomar, é só entrar: lançar uma compra leva alguns segundos.",
        ],
        Button: new EmailButton("Entrar no Prisma Money", signInLink, ShowLinkFallback: false),
        Note: "Se o Prisma não faz mais sentido para você, tudo bem. Dá para apagar a conta pelo menu do app, e tudo sai na hora.");

    // O e-mail do modelo com dados de exemplo (a prévia e o teste): nunca uma pessoa de verdade, nem um token que valha.
    public static EmailTemplate Example(CampaignTemplate template, Uri publicUrl, DateTime nowUtc) => template.Kind switch
    {
        EmailKind.ConfirmEmailReminder => ConfirmReminder(
            ExampleAddress, AuthEmails.ConfirmEmailLink(publicUrl, ExampleUserId, "exemplo"), nowUtc + ConfirmEmailTokenProvider.Lifespan),
        EmailKind.MissYou => MissYou(ExampleAddress, AuthEmails.SignInLink(publicUrl)),
        _ => throw new ArgumentOutOfRangeException(nameof(template)),
    };
}

// O lembrete de confirmação, montado na hora do envio com um token novo. Quem confirmou entre o pedido e o envio não recebe.
public sealed class ConfirmEmailReminderComposer(UserManager<AppUser> users, IOptions<AppOptions> app, IClock clock) : IEmailComposer
{
    public EmailKind Kind => EmailKind.ConfirmEmailReminder;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(email.UserId.ToString());
        if (user is null || user.EmailConfirmed)
            return null;

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var publicUrl = app.Value.PublicUrl;
        var link = AuthEmails.ConfirmEmailLink(publicUrl, user.Id, token);
        return EmailLayout.Render(
            CampaignEmails.ConfirmReminder(email.ToAddress, link, clock.UtcNow + ConfirmEmailTokenProvider.Lifespan), publicUrl);
    }
}

// "Sentimos sua falta". Quem voltou depois de o e-mail entrar na fila não recebe. O descadastro ainda não existe, então o
// catálogo bloqueia o modelo (EmailCampaignCatalog.UnsubscribeAvailable); o rodapé de descadastro entra com ele.
public sealed class MissYouComposer(UserManager<AppUser> users, IOptions<AppOptions> app) : IEmailComposer
{
    public EmailKind Kind => EmailKind.MissYou;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(email.UserId.ToString());
        if (user is null || !user.EmailConfirmed || user.LastSeenAt > email.CreatedAt)
            return null;

        var publicUrl = app.Value.PublicUrl;
        return EmailLayout.Render(CampaignEmails.MissYou(email.ToAddress, AuthEmails.SignInLink(publicUrl)), publicUrl);
    }
}
