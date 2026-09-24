using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure.Auth;

namespace Prisma.Api.Infrastructure;

// IdentityUserContext, e não IdentityDbContext, porque a aplicação não usa roles.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Nomes curtos em vez de asp_net_users etc. O snake_case vem da convenção global.
        builder.Entity<AppUser>().ToTable("users");

        // O Identity nomeia estes dois índices explicitamente, o que escapa da convenção.
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
        builder.Entity<AppUser>().HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}
