using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Accounts;

public static class ListAccounts
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<AccountResponse>> Execute(CancellationToken ct) =>
            await db.Accounts
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .Select(AccountResponse.Projection)
                .ToListAsync(ct);
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) =>
            Results.Ok(await handler.Execute(ct)));
}
