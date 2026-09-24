using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Auth;

public static class Me
{
    public sealed record Response(Guid Id, string Email);

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<Response>> Execute(CancellationToken ct)
        {
            var response = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == currentUser.UserId)
                .Select(u => new Response(u.Id, u.Email!))
                .SingleOrDefaultAsync(ct);

            return response is null
                ? new Error(ErrorType.NotFound, "Usuário não encontrado.")
                : response;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/me", async (Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });
}
