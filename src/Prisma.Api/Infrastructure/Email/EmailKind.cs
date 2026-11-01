namespace Prisma.Api.Infrastructure.Email;

// Os e-mails que o Prisma manda (docs/email.md, seção 4). Gravado como texto na fila.
public enum EmailKind
{
    ConfirmEmail,
    AlreadyRegistered,
    ResetPassword,
    PasswordChanged,

    // Disparos do admin (docs/admin-email.md, seção 4): saem de um modelo e um público escolhidos pelo dono, não de um pedido da pessoa.
    ConfirmEmailReminder,
    MissYou,
}
