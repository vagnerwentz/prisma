using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// Monta o e-mail de confirmação na hora do envio, com um token novo (docs/email.md, seção 3, regra 2). Quem já
// confirmou (por um e-mail anterior) não recebe outro.
public sealed class ConfirmEmailComposer(UserManager<AppUser> users, IOptions<AppOptions> app, IClock clock) : IEmailComposer
{
    public EmailKind Kind => EmailKind.ConfirmEmail;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(email.UserId.ToString());
        if (user is null || user.EmailConfirmed)
            return null;

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var publicUrl = app.Value.PublicUrl;
        var link = AuthEmails.ConfirmEmailLink(publicUrl, user.Id, token);
        return EmailLayout.Render(
            AuthEmails.ConfirmEmail(email.ToAddress, link, clock.UtcNow + ConfirmEmailTokenProvider.Lifespan), publicUrl);
    }
}

// Aviso ao dono de um e-mail já cadastrado de que alguém tentou criar outra conta com ele (docs/email.md, 5.1).
public sealed class AlreadyRegisteredComposer(UserManager<AppUser> users, IOptions<AppOptions> app) : IEmailComposer
{
    public EmailKind Kind => EmailKind.AlreadyRegistered;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        if (await users.FindByIdAsync(email.UserId.ToString()) is null)
            return null;

        var publicUrl = app.Value.PublicUrl;
        return EmailLayout.Render(
            AuthEmails.AlreadyRegistered(email.ToAddress, AuthEmails.SignInLink(publicUrl), AuthEmails.ForgotPasswordLink(publicUrl)),
            publicUrl);
    }
}

// "Esqueci a senha" (docs/email.md, 5.2): o link de senha nova, com um token gerado na hora do envio.
public sealed class ResetPasswordComposer(UserManager<AppUser> users, IOptions<AppOptions> app, IClock clock) : IEmailComposer
{
    public EmailKind Kind => EmailKind.ResetPassword;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(email.UserId.ToString());
        if (user is null)
            return null;

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var publicUrl = app.Value.PublicUrl;
        var link = AuthEmails.ResetPasswordLink(publicUrl, user.Id, token);
        return EmailLayout.Render(
            AuthEmails.ResetPassword(email.ToAddress, link, clock.UtcNow + ResetPasswordTokenProvider.Lifespan), publicUrl);
    }
}

// Aviso de que a senha foi trocada, com a hora do pedido (a da troca), e o caminho para quem não reconhece a troca.
public sealed class PasswordChangedComposer(UserManager<AppUser> users, IOptions<AppOptions> app) : IEmailComposer
{
    public EmailKind Kind => EmailKind.PasswordChanged;

    public async Task<EmailContent?> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        if (await users.FindByIdAsync(email.UserId.ToString()) is null)
            return null;

        var publicUrl = app.Value.PublicUrl;
        return EmailLayout.Render(
            AuthEmails.PasswordChanged(email.ToAddress, email.CreatedAt, AuthEmails.ForgotPasswordLink(publicUrl)), publicUrl);
    }
}
