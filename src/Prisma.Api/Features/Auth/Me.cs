using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

public static class Me
{
    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, IOptions<AdminOptions> admins)
    {
        public async Task<Result<SessionUser>> Execute(CancellationToken ct)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, ct);

            // Conta apagada em outro aparelho (etapa A.3): o cookie daqui ainda vale até a próxima conferência
            // (AuthSetup.SessionCheckInterval). 401, e não 404, para a tela levar ao login.
            return user is null
                ? new Error(ErrorType.Unauthorized, "Sessão expirada. Entre de novo.")
                : SessionUser.Of(user, admins.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/me", async (Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<SessionUser>(200)
            .ProducesProblem(401);
}
