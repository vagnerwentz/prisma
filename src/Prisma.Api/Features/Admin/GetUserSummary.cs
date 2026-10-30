using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Features.Admin;

// Quantas pessoas há e quantas não aparecem há N dias (docs/admin-email.md, AE.2). Só contagem: nenhum e-mail, id ou data de
// uma pessoa sai daqui. Uma agregação em SQL, sem carregar conta nenhuma; a regra de "há N dias" é do domínio (ActivityWindow).
public static class GetUserSummary
{
    public sealed class Handler(AppDbContext db, IClock clock, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public async Task<Result<UserSummaryResponse>> Execute(int? days, CancellationToken ct)
        {
            var window = days is null ? ActivityWindow.Default : ActivityWindow.Of(days.Value);
            if (!window.IsSuccess)
                return window.Error;

            // O pedido de uma janela válida vai ao log: quem olhou a contagem fica só aqui (como o disparo do admin do mercado).
            logger.UserSummaryRequested(currentUser.UserId, window.Value.Days);
            var activeSince = window.Value.ActiveSince(clock.Today);

            // Um grupo só (a tabela inteira); sem contas, nenhum grupo e tudo zero.
            var counts = await db.Users.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Accounts = g.Count(),
                    Confirmed = g.Count(u => u.EmailConfirmed),
                    Active = g.Count(u => u.EmailConfirmed && u.LastSeenAt >= activeSince),
                    Inactive = g.Count(u => u.EmailConfirmed && u.LastSeenAt < activeSince),
                    NeverUsed = g.Count(u => u.EmailConfirmed && u.LastSeenAt == null),
                })
                .SingleOrDefaultAsync(ct);

            return counts is null
                ? new UserSummaryResponse(window.Value.Days, 0, 0, 0, 0, 0, 0)
                : new UserSummaryResponse(
                    window.Value.Days, counts.Accounts, counts.Confirmed, counts.Accounts - counts.Confirmed,
                    counts.Active, counts.Inactive, counts.NeverUsed);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/summary", async (int? days, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(days, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<UserSummaryResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);
}
