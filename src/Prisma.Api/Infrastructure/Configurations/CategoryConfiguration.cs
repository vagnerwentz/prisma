using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Categories;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public const string SiblingNameIndex = "ux_categories_sibling_name";

    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", table =>
            table.HasCheckConstraint("ck_categories_type", "type IN ('Income', 'Expense')"));

        // citext: comparação sem diferenciar maiúsculas, usada pelo índice único abaixo.
        builder.Property(c => c.Name).HasColumnType("citext");
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Icon).HasMaxLength(Category.IconMaxLength);
        builder.Property(c => c.Color).HasMaxLength(7);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);

        // Nome único entre irmãos (mesmo usuário, tipo e pai), ignorando excluídas. NULLS NOT
        // DISTINCT faz duas categorias raiz (pai nulo) com o mesmo nome colidirem.
        builder.HasIndex(c => new { c.UserId, c.Type, c.ParentCategoryId, c.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(SiblingNameIndex);
    }
}
