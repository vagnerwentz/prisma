using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Prisma.Api.Infrastructure.Auth;

// Token do link "criar uma senha nova", que vale 1 hora (docs/email.md, seção 4): quem pediu está com o e-mail aberto, e
// um link de senha que vive dias é uma chave esquecida na caixa de entrada. Como o da confirmação, não é gravado e
// deixa de valer quando o SecurityStamp muda (a própria troca de senha muda).
public sealed class ResetPasswordTokenProvider(
    IDataProtectionProvider dataProtection,
    IOptions<ResetPasswordTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtection, options, logger)
{
    public const string ProviderName = "ResetPassword";
    public static readonly TimeSpan Lifespan = TimeSpan.FromHours(1);
}

public sealed class ResetPasswordTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public ResetPasswordTokenProviderOptions()
    {
        Name = ResetPasswordTokenProvider.ProviderName;
        TokenLifespan = ResetPasswordTokenProvider.Lifespan;
    }
}
