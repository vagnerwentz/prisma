using System.Security.Claims;

namespace Prisma.Api.Infrastructure.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Não há usuário autenticado nesta requisição.");
}
