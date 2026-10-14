using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Prisma.Api.Infrastructure.Auth;

// Token do link de confirmação de e-mail, que vale 3 dias (docs/email.md, seção 4). Provedor próprio porque o padrão
// do Identity usa o mesmo prazo para todos os links, e o de senha nova vale 1 hora (H.4c). O token não é gravado: vale
// pela assinatura (Data Protection, chaves no Postgres) e pelo SecurityStamp do usuário, que muda ao confirmar.
public sealed class ConfirmEmailTokenProvider(
    IDataProtectionProvider dataProtection,
    IOptions<ConfirmEmailTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtection, options, logger)
{
    public const string ProviderName = "ConfirmEmail";
    public static readonly TimeSpan Lifespan = TimeSpan.FromDays(3);
}

public sealed class ConfirmEmailTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ConfirmEmailTokenProviderOptions()
    {
        Name = ConfirmEmailTokenProvider.ProviderName;
        TokenLifespan = ConfirmEmailTokenProvider.Lifespan;
    }
}
