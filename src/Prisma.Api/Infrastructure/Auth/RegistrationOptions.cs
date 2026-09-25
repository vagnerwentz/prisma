namespace Prisma.Api.Infrastructure.Auth;

// Quem pode criar conta. Em produção o cadastro fica fechado até existirem termos de uso e
// confirmação de e-mail (PLAN.md, pendências); só os e-mails liberados passam.
public sealed class RegistrationOptions
{
    public const string Section = "Registration";

    public bool Open { get; init; }

    public string[] AllowedEmails { get; init; } = [];

    public bool Allows(string email) =>
        Open || AllowedEmails.Any(allowed => string.Equals(allowed.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
