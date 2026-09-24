using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;

namespace Prisma.Api.Infrastructure;

// IdentityUserContext, e não IdentityDbContext, porque a aplicação não usa roles.
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    IClock clock)
    : IdentityUserContext<AppUser, Guid>(options)
{
    // Filtros nomeados (EF Core 10): permitem ignorar só o soft delete, por exemplo para
    // restaurar um registro, sem nunca desligar o isolamento por usuário.
    public const string OwnerFilter = "Owner";
    public const string SoftDeleteFilter = "SoftDelete";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    // Lido pelo EF a cada consulta, não no momento em que o modelo é construído.
    private Guid CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("citext");

        // Nomes curtos em vez de asp_net_users etc. O snake_case vem da convenção global.
        builder.Entity<AppUser>().ToTable("users");

        // O Identity nomeia estes dois índices explicitamente, o que escapa da convenção.
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Toda entidade do domínio recebe os filtros automaticamente (CLAUDE.md, regras 6 e 7).
        var applyFilters = typeof(AppDbContext)
            .GetMethod(nameof(ApplyEntityFilters), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in builder.Model.GetEntityTypes()
                     .Select(t => t.ClrType)
                     .Where(t => typeof(Entity).IsAssignableFrom(t))
                     .ToList())
            applyFilters.MakeGenericMethod(type).Invoke(this, [builder]);
    }

    private void ApplyEntityFilters<TEntity>(ModelBuilder builder) where TEntity : Entity =>
        builder.Entity<TEntity>()
            .HasQueryFilter(OwnerFilter, e => e.UserId == CurrentUserId)
            .HasQueryFilter(SoftDeleteFilter, e => e.DeletedAt == null);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditing();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Datas de auditoria vêm do IClock; Remove vira soft delete (CLAUDE.md, regra 7).
    private void ApplyAuditing()
    {
        var now = clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.UserId != CurrentUserId)
                        throw new InvalidOperationException("Entidade criada para outro usuário que não o autenticado.");
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(e => e.DeletedAt).CurrentValue = now;
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
            }
        }
    }
}
