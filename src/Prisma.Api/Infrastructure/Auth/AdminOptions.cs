using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Prisma.Api.Infrastructure.Auth;

// Quem é admin (docs/admin-mercado.md, M1): uma lista de e-mails na configuração (variável de ambiente `Admin__Emails__0`), nunca
// no repositório. Falha fechada: lista vazia, ausente ou com e-mail que não parece e-mail = ninguém é admin (e, no último caso, a
// API nem sobe). Vale só com o e-mail confirmado, sem diferenciar maiúsculas.
public sealed class AdminOptions
{
    public const string Section = "Admin";

    public const string PolicyName = "Admin";

    public string[] Emails { get; init; } = [];

    // Quanto a tela espera uma sincronização manual antes de desistir (docs/admin-mercado.md, M3).
    public int SyncTimeoutSeconds { get; init; } = 90;

    public bool IsValid() =>
        SyncTimeoutSeconds is >= 1 and <= 300
        && Emails.All(e => !string.IsNullOrWhiteSpace(e) && e.Trim().Contains('@') && !e.Trim().Any(char.IsWhiteSpace));

    public bool IsAdmin(AppUser user) =>
        user.EmailConfirmed
        && user.Email is { } email
        && Emails.Any(admin => string.Equals(admin.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class AdminRequirement : IAuthorizationRequirement;

// Confere a cada pedido, no banco, que quem está na sessão ainda é admin: e-mail trocado, confirmação desfeita ou conta apagada
// derrubam o acesso sem esperar o cookie vencer.
public sealed class AdminAuthorizationHandler(UserManager<AppUser> users, IOptions<AdminOptions> options)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var user = await users.GetUserAsync(context.User);
        if (user is not null && options.Value.IsAdmin(user))
            context.Succeed(requirement);
    }
}
