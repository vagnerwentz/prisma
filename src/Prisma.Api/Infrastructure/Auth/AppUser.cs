using Microsoft.AspNetCore.Identity;

namespace Prisma.Api.Infrastructure.Auth;

// Chave Guid porque toda entidade do domínio referencia o dono por UserId (Guid).
public sealed class AppUser : IdentityUser<Guid>
{
    // Versão do catálogo de categorias padrão que o usuário já recebeu (docs/fase-2.md, 2.11). Quem se
    // cadastrou antes da 2.23 está na 1; o cadastro grava a versão atual.
    public int CategoryCatalogVersion { get; set; }
}
