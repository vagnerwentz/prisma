namespace Prisma.Api.Infrastructure.Email;

// Os e-mails que o Prisma manda (docs/email.md, seção 4). Gravado como texto na fila.
public enum EmailKind
{
    ConfirmEmail,
    AlreadyRegistered,
    ResetPassword,
    PasswordChanged,
}
