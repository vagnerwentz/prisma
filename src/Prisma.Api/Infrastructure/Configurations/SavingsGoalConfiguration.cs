using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Configurations;

// Objetivos financeiros, as caixas do investimento (docs/renda-fixa.md, 2.2). Da pessoa: dono, filtro e soft delete.
public sealed class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public const string NameIndex = "ux_savings_goals_user_name";

    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        // As invariantes vivem no domínio; as constraints são a segunda linha de defesa.
        builder.ToTable("savings_goals", table =>
        {
            table.HasCheckConstraint("ck_savings_goals_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_savings_goals_target", "target_cents IS NULL OR target_cents > 0");
            table.HasCheckConstraint("ck_savings_goals_default_percent",
                "default_index_percent_e4 IS NULL OR default_index_percent_e4 BETWEEN 100 AND 5000000");
        });

        // citext: comparação sem diferenciar maiúsculas, usada pelo índice único abaixo (o tamanho é conferido no domínio).
        builder.Property(g => g.Name).HasColumnType("citext");
        builder.Property(g => g.Description).HasMaxLength(SavingsGoal.DescriptionMaxLength);

        // Editar enquanto outro pedido edita ou exclui o mesmo objetivo dá 409 (CLAUDE.md, seção 6).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Restrict);

        // Um nome por pessoa entre os objetivos vivos, sem diferenciar maiúsculas: "casamento" e "Casamento" são o mesmo.
        // O excluído não conta, para poder criar de novo com o nome (e o "Desfazer" confere o conflito).
        builder.HasIndex(g => new { g.UserId, g.Name })
            .HasDatabaseName(NameIndex)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
