using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Auth;

public static class AuthSetup
{
    public const string RateLimitPolicy = "auth";

    // Relatos de erro do navegador (etapa H.3b): abertos sem login, então limitados por IP.
    public const string ClientErrorsRateLimitPolicy = "client-errors";

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Chaves do cookie no Postgres. O nome fixo impede que o isolamento padrão, pela pasta do
        // app, invalide as sessões quando o app muda de pasta (outro build, outro contêiner).
        services.AddDataProtection()
            .SetApplicationName("Prisma")
            .PersistKeysToDbContext<AppDbContext>();

        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.Section));

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Caminho fixo: sem isso o cookie herdaria o /api da API (PathBase) e conviveria com um
            // cookie antigo de mesmo nome em "/". O navegador manda os dois, e o ASP.NET lê o último,
            // o velho: o login parecia funcionar e a sessão caía em seguida.
            options.Cookie.Path = "/";

            // API não redireciona para tela de login: responde com o status.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Todo endpoint exige login, salvo os marcados com AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 5);
        var window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0,
                    }));

            var clientErrorsLimit = configuration.GetValue("RateLimiting:ClientErrors:PermitLimit", 10);
            options.AddPolicy(ClientErrorsRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = clientErrorsLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Prisma.Api.RateLimiting")
                    .RateLimited(context.HttpContext.Request.Method, AppLog.RouteOf(context.HttpContext));

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                await Results.Problem(
                        detail: "Muitas tentativas. Aguarde um minuto e tente novamente.",
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Muitas tentativas.")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }
}
