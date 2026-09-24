using Microsoft.AspNetCore.Identity;

namespace Prisma.Api.Infrastructure.Auth;

// Chave Guid porque toda entidade do domínio referencia o dono por UserId (Guid).
public sealed class AppUser : IdentityUser<Guid>;
