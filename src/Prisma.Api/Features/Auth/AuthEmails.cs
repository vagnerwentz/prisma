using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Prisma.Api.Infrastructure.Email;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

// Os e-mails da conta (docs/email.md, seção 4): o texto de cada um, e os links. O layout é o EmailLayout.
public static class AuthEmails
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Depois do "#": o navegador nunca manda essa parte a servidor nenhum, então o token não fica em log de acesso (do
    // Railway, de proxy) nem vai no Referer. A tela lê o fragmento e manda o token no corpo do POST. Em base64url: o
    // token do Identity tem "+" e "/", que a URL estragaria.
    public static Uri ConfirmEmailLink(Uri publicUrl, Guid userId, string token) =>
        TokenLink(publicUrl, "confirmar-email", userId, token);

    public static Uri ResetPasswordLink(Uri publicUrl, Guid userId, string token) =>
        TokenLink(publicUrl, "nova-senha", userId, token);

    public static Uri ForgotPasswordLink(Uri publicUrl) => new(publicUrl, "esqueci-senha");

    private static Uri TokenLink(Uri publicUrl, string screen, Guid userId, string token) =>
        new(publicUrl, $"{screen}#u={userId:D}&t={WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))}");

    // Nulo: o texto não é um token deste formato (link cortado ou mexido).
    public static string? DecodeToken(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static EmailTemplate ConfirmEmail(string address, Uri link, DateTime expiresAtUtc) => new(
        Subject: "Confirme seu e-mail no Prisma Money",
        Preheader: "Falta um passo para começar a usar o Prisma Money.",
        Eyebrow: "Confirmação de e-mail",
        Title: "Falta só confirmar seu e-mail",
        Paragraphs:
        [
            $"Recebemos um cadastro no Prisma Money com o e-mail **{address}**.",
            "Toque no botão e digite a senha que você criou no cadastro. Você já entra no app em seguida.",
        ],
        Button: new EmailButton("Confirmar e-mail", link),
        Details:
        [
            new EmailDetail("Conta", address),
            new EmailDetail("Link válido até", SaoPaulo(expiresAtUtc)),
        ],
        Note: "Não foi você? É só ignorar este e-mail. Sem a confirmação, a conta não é ativada.");

    public static EmailTemplate AlreadyRegistered(string address, Uri signInLink, Uri forgotPasswordLink) => new(
        Subject: "Alguém tentou criar uma conta com seu e-mail",
        Preheader: "Sua conta continua como estava. Veja o que fazer.",
        Eyebrow: "Aviso de cadastro",
        Title: "Você já tem uma conta no Prisma Money",
        Paragraphs:
        [
            $"Alguém tentou criar uma conta nova com o e-mail **{address}**.",
            "Como este e-mail já tem cadastro, nenhuma conta nova foi criada e nada mudou na sua.",
        ],
        Button: new EmailButton("Entrar no Prisma Money", signInLink, ShowLinkFallback: false),
        Note: $"Foi você? É só entrar com sua senha, ou [crie uma senha nova]({forgotPasswordLink}) se não lembra. Não foi? " +
              "Pode ignorar este e-mail: ninguém recebeu acesso à sua conta.");

    public static EmailTemplate ResetPassword(string address, Uri link, DateTime expiresAtUtc) => new(
        Subject: "Crie uma senha nova no Prisma Money",
        Preheader: "O link vale por 1 hora.",
        Eyebrow: "Senha nova",
        Title: "Crie uma senha nova",
        Paragraphs:
        [
            $"Recebemos um pedido para criar uma senha nova para a conta **{address}**.",
            "Toque no botão e escolha a senha. Você já entra no app em seguida.",
        ],
        Button: new EmailButton("Criar senha nova", link),
        Details:
        [
            new EmailDetail("Conta", address),
            new EmailDetail("Link válido até", SaoPaulo(expiresAtUtc)),
        ],
        Note: "Não foi você? É só ignorar este e-mail. Sua senha atual continua valendo.");

    public static EmailTemplate PasswordChanged(string address, DateTime changedAtUtc, Uri forgotPasswordLink) => new(
        Subject: "Sua senha do Prisma Money foi trocada",
        Preheader: "Se não foi você, crie uma senha nova agora.",
        Eyebrow: "Aviso de segurança",
        Title: "Sua senha foi trocada",
        Paragraphs:
        [
            $"A senha da conta **{address}** acabou de ser trocada.",
            "Os outros aparelhos em que a conta estava aberta saem sozinhos em alguns minutos.",
        ],
        Details:
        [
            new EmailDetail("Conta", address),
            new EmailDetail("Quando", SaoPaulo(changedAtUtc)),
        ],
        Note: $"Não foi você? [Crie uma senha nova agora]({forgotPasswordLink}). Ela desfaz a troca e tira quem entrou.");

    public static Uri SignInLink(Uri publicUrl) => new(publicUrl, "entrar");

    // "16/10/2026, às 21:31", na hora de São Paulo (CLAUDE.md, regra 3).
    public static string SaoPaulo(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, SaoPauloTime.Zone).ToString("dd/MM/yyyy', às 'HH:mm", PtBr);
}
