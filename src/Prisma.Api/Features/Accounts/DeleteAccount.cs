using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Accounts;

public static class DeleteAccount
{
    public sealed class Handler(AppDbContext db)
    {
        // Remove vira soft delete no AppDbContext.
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (account is null)
                return new Error(ErrorType.NotFound, "Conta não encontrada.");

            db.Accounts.Remove(account);
            await db.SaveChangesAsync(ct);
            return account.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        });
}
