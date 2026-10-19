using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Privacy;

namespace Prisma.Api.Features.Auth;

// Quem está na sessão: a resposta de /auth/me e de tudo que entra (login, confirmação do e-mail, senha nova, aceite).
// MustAcceptTerms leva a tela ao aceite dos termos antes do app (etapa A.4). IsAdmin mostra a entrada da tela de admin
// (docs/admin-mercado.md); quem manda é o servidor, que confere de novo a cada pedido de /admin.
public sealed record SessionUser(Guid Id, string Email, bool MustAcceptTerms, bool IsAdmin)
{
    public static SessionUser Of(AppUser user, AdminOptions admins) =>
        new(user.Id, user.Email!, LegalTerms.MustAccept(user), admins.IsAdmin(user));
}
