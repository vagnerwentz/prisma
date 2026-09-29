using System.Security.Claims;

namespace Prisma.Api.Infrastructure.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
}

// Usuário fixo num escopo de DI fora de uma requisição: a tarefa que gera os lançamentos que se repetem
// processa cada usuário no seu próprio escopo (docs/fase-2.md, 2.14, A5), e o filtro de dono e a trava
// de gravação continuam valendo. Numa requisição fica vazio, e vale o usuário do cookie.
public sealed class ScopedUser
{
    public Guid? UserId { get; set; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor, ScopedUser scoped) : ICurrentUser
{
    public Guid UserId =>
        scoped.UserId
        ?? (Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Não há usuário autenticado nesta requisição."));
}
