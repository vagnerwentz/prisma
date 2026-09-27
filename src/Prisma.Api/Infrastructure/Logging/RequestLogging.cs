using System.Diagnostics;
using System.Security.Claims;
using Prisma.Api.Infrastructure.Http;

namespace Prisma.Api.Infrastructure.Logging;

public static class RequestLogging
{
    // Uma linha por requisição da API, escrita quando o pipeline termina (inclusive depois do
    // tratamento de exceção, com o status final). Só a API: arquivos e telas do frontend não entram.
    public static void UseRequestLogging(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prisma.Api.Requests");

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(FrontendHosting.ApiPrefix))
            {
                await next(context);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                using (UserScope(logger, context.User))
                    logger.RequestFinished(
                        context.Request.Method, AppLog.RouteOf(context), context.Response.StatusCode, Math.Round(elapsed, 1));
            }
        });
    }

    // Depois da autenticação: todo log escrito durante a requisição leva o userId (escopo).
    public static void UseUserLogScope(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prisma.Api.Requests");

        app.Use(async (context, next) =>
        {
            using (UserScope(logger, context.User))
                await next(context);
        });
    }

    // Sem usuário (anônimo), sem escopo: a linha não ganha "userId": null.
    private static IDisposable? UserScope(ILogger logger, ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? logger.BeginScope(new[] { new KeyValuePair<string, object?>("UserId", id) })
            : null;
}
