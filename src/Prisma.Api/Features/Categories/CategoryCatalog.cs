using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Domain.Categories;

namespace Prisma.Api.Features.Categories;

// Entrega ao usuário as categorias das versões novas do catálogo (docs/fase-2.md, 2.11). Roda quando o
// app pede a lista de categorias: só para o próprio usuário (o filtro de dono vale como sempre) e só
// quando a versão dele é antiga. Nas outras vezes custa uma consulta de um número.
//
// É a única escrita dentro de uma consulta, de propósito: sincronizar no login não bastaria, porque a
// sessão é persistente e a pessoa pode ficar semanas sem logar de novo.
public sealed class CategoryCatalog(AppDbContext db, ICurrentUser currentUser)
{
    public async Task EnsureCurrent(CancellationToken ct)
    {
        var version = await db.Users.Where(u => u.Id == currentUser.UserId)
            .Select(u => u.CategoryCatalogVersion)
            .SingleAsync(ct);
        if (version >= DefaultCategories.Version)
            return;

        var user = await db.Users.SingleAsync(u => u.Id == currentUser.UserId, ct);
        var active = await db.Categories.ToListAsync(ct);
        var sync = DefaultCategories.Sync(user.Id, user.CategoryCatalogVersion, active);

        db.Categories.AddRange(sync.Added);
        user.CategoryCatalogVersion = sync.Version;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.IsUniqueViolation(CategoryConfiguration.TemplateKeyIndex) ||
            ex.IsUniqueViolation(CategoryConfiguration.SiblingNameIndex))
        {
            // Outra aba sincronizou primeiro: nada foi gravado aqui, e a lista lê o que ela gravou.
            db.ChangeTracker.Clear();
        }
    }
}
