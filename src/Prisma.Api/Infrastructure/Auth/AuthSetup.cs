using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;

namespace Prisma.Api.Infrastructure.Auth;

public static class AuthSetup
{
    public const string RateLimitPolicy = "auth";

    // Relatos de erro do navegador (etapa H.3b): abertos sem login, então limitados por IP.
    public const string ClientErrorsRateLimitPolicy = "client-errors";

    // "Avise a gente" (I.7c3): exige login, então limitado por pessoa, não por IP.
    public const string AssetRequestsRateLimitPolicy = "asset-requests";

    // Disparos manuais da sincronização dos dados de mercado (docs/admin-mercado.md, 6): por pessoa, não por IP.
    public const string AdminSyncRateLimitPolicy = "admin-sync";

    public static readonly TimeSpan SessionCheckInterval = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Chaves do cookie no Postgres. O nome fixo impede que o isolamento padrão, pela pasta do
        // app, invalide as sessões quando o app muda de pasta (outro build, outro contêiner).
        services.AddDataProtection()
            .SetApplicationName("Prisma")
            .PersistKeysToDbContext<AppDbContext>();

        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.Section));
        services.AddOptions<AdminOptions>()
            .Bind(configuration.GetSection(AdminOptions.Section))
            .Validate(o => o.IsValid(), "Configuração Admin inválida: Admin:Emails precisa ter só e-mails e SyncTimeoutSeconds ficar de 1 a 300.")
            .ValidateOnStart();
        services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.Section))
            .Validate(o => o.IsValid(), "Configuração App inválida: App:PublicUrl precisa ser a raiz do site (https://dominio/).")
            .ValidateOnStart();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // Sem confirmar o e-mail, não entra (docs/email.md, 5.1). O Login confere a senha antes, para que o
                // estado da conta só apareça para quem a sabe.
                options.SignIn.RequireConfirmedEmail = true;
                options.Tokens.EmailConfirmationTokenProvider = ConfirmEmailTokenProvider.ProviderName;
                options.Tokens.PasswordResetTokenProvider = ResetPasswordTokenProvider.ProviderName;

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
            .AddUserManager<AppUserManager>()
            .AddSignInManager()
            .AddTokenProvider<ConfirmEmailTokenProvider>(ConfirmEmailTokenProvider.ProviderName)
            .AddTokenProvider<ResetPasswordTokenProvider>(ResetPasswordTokenProvider.ProviderName)
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();

        // De quanto em quanto tempo o cookie confere o SecurityStamp (docs/email.md, 5.4). Trocar a senha muda o selo, e a
        // sessão aberta em outro aparelho cai na conferência seguinte: em até 5 minutos, não nos 30 do padrão. Custa uma
        // consulta ao usuário por sessão a cada 5 minutos.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = SessionCheckInterval);

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
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminOptions.PolicyName, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));

        services.AddHttpContextAccessor();
        services.AddScoped<ScopedUser>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 5);
        var window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress.PartitionKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0,
                    }));

            var clientErrorsLimit = configuration.GetValue("RateLimiting:ClientErrors:PermitLimit", 10);
            options.AddPolicy(ClientErrorsRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress.PartitionKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = clientErrorsLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            var assetRequestsLimit = configuration.GetValue("RateLimiting:AssetRequests:PermitLimit", 10);
            options.AddPolicy(AssetRequestsRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = assetRequestsLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            var adminSyncLimit = configuration.GetValue("RateLimiting:AdminSync:PermitLimit", 6);
            options.AddPolicy(AdminSyncRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = adminSyncLimit,
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
