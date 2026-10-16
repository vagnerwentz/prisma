using Microsoft.AspNetCore.Identity;

namespace Prisma.Api.Infrastructure.Auth;

// Chave Guid porque toda entidade do domínio referencia o dono por UserId (Guid).
public sealed class AppUser : IdentityUser<Guid>
{
    // Versão do catálogo de categorias padrão que o usuário já recebeu (docs/fase-2.md, 2.11). Quem se
    // cadastrou antes da 2.23 está na 1; o cadastro grava a versão atual.
    public int CategoryCatalogVersion { get; set; }

    // Quando a conta nasceu (UTC). O AppDbContext carimba na criação, com o IClock. Contas de antes da coluna foram
    // preenchidas pela migration com o registro mais antigo que a pessoa ainda tem: é uma data aproximada (no máximo
    // a real), não exata.
    [PersonalData]
    public DateTime CreatedAt { get; set; }

    // Aceite dos termos de uso e da política de privacidade (etapa A.4, LegalTerms): as versões aceitas e quando. Vazio
    // em quem se cadastrou antes do aceite existir: essa pessoa aceita ao entrar.
    [PersonalData]
    public string? TermsVersion { get; set; }

    [PersonalData]
    public string? PrivacyVersion { get; set; }

    [PersonalData]
    public DateTime? TermsAcceptedAt { get; set; }

    // Quando a última senha errada chegou (etapa H.4e, AppUserManager): a próxima só soma com as anteriores se vier até
    // 15 minutos depois. Segurança da conta, fora da cópia dos dados (ops/privacy/export-user.sql).
    public DateTime? LastAccessFailedAt { get; set; }
}
