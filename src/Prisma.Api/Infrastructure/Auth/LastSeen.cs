using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Auth;

// Carimba o último acesso (AppUser.LastSeenAt) no primeiro pedido autenticado de cada dia em São Paulo. Pelo pedido, e
// não só pelo login, porque o cookie é persistente: quem fica semanas com a sessão aberta nunca passa pelo login.
public sealed class LastSeenStamper(AppDbContext db, ILastSeenCache cache, IClock clock)
{
    public async Task Execute(Guid userId, CancellationToken ct)
    {
        var today = clock.Today;
        if (await cache.WasStampedAsync(userId, today))
            return;

        // Um UPDATE só, sem carregar a entidade: não mexe em UpdatedAt nem no token de concorrência, e dois pedidos ao
        // mesmo tempo não brigam (o segundo não encontra linha a mudar). Conta apagada em outro aparelho: zero linhas.
        var startOfToday = SaoPauloTime.StartOf(today);
        var now = clock.UtcNow;
        await db.Users
            .Where(u => u.Id == userId && (u.LastSeenAt == null || u.LastSeenAt < startOfToday))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, now), ct);

        // Só depois de gravar: se o UPDATE falhar, o próximo pedido tenta de novo.
        await cache.RememberAsync(userId, today);
    }
}

public static class LastSeenMiddleware
{
    // Depois da autenticação e do escopo de log (o aviso leva o userId). Daqui em diante só chegam pedidos a /api
    // (FrontendHosting.UseFrontendAndApiPrefix). Falhar ao carimbar nunca derruba o pedido: o dado é de apoio.
    public static void UseLastSeen(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                try
                {
                    await context.RequestServices.GetRequiredService<LastSeenStamper>().Execute(userId, context.RequestAborted);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Prisma.Api.LastSeen")
                        .LastSeenStampFailed(exception);
                }
            }

            await next(context);
        });
}
