using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Privacy;

namespace Prisma.Api.Features.Auth;

// Quem está na sessão: a resposta de /auth/me e de tudo que entra (login, confirmação do e-mail, senha nova, aceite).
// MustAcceptTerms leva a tela ao aceite dos termos antes do app (etapa A.4).
public sealed record SessionUser(Guid Id, string Email, bool MustAcceptTerms)
{
    public static SessionUser Of(AppUser user) => new(user.Id, user.Email!, LegalTerms.MustAccept(user));
}
