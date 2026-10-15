using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prisma.Domain;

namespace Prisma.Api.Infrastructure.Auth;

// O contador de senhas erradas com prazo (etapa H.4e). O do Identity só zera com a senha certa ou com o bloqueio: quem
// erra uma vez hoje e outra semana que vem acabaria bloqueado por uma soma de semanas. Aqui a senha errada só soma com as
// anteriores se vier até 15 minutos depois da última; mais tarde, a contagem recomeça.
//
// Fica no AccessFailedAsync para valer em todo lugar que pede a senha (entrar, confirmar o e-mail, trocar a senha,
// apagar a conta), sem cada um lembrar da regra. O fim do bloqueio continua com o Identity, que lê o relógio do sistema.
public sealed class AppUserManager(
    IUserStore<AppUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<AppUser> passwordHasher,
    IEnumerable<IUserValidator<AppUser>> userValidators,
    IEnumerable<IPasswordValidator<AppUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<AppUser>> logger,
    IClock clock)
    : UserManager<AppUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer,
        errors, services, logger)
{
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    public override async Task<IdentityResult> AccessFailedAsync(AppUser user)
    {
        var now = clock.UtcNow;
        if (user.LastAccessFailedAt is { } last && now - last >= FailureWindow)
            await ((IUserLockoutStore<AppUser>)Store).ResetAccessFailedCountAsync(user, CancellationToken);

        // Gravada junto com a contagem: o AccessFailedAsync do Identity salva o usuário inteiro.
        user.LastAccessFailedAt = now;
        return await base.AccessFailedAsync(user);
    }
}
