using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

// Eventos da empresa (docs/investimentos.md, I.7c1): dado de mercado, igual para todos, sem dono (CLAUDE.md, regra 6,
// exceção dos dados de mercado). AppDbContext.GlobalTables lista esta tabela. Só a curadoria escreve.
public sealed class CorporateEventConfiguration : IEntityTypeConfiguration<CorporateEvent>
{
    public const string KeyIndex = "ux_corporate_events_key";

    public void Configure(EntityTypeBuilder<CorporateEvent> builder)
    {
        var kinds = string.Join(", ", Enum.GetNames<CorporateEventKind>().Select(k => $"'{k}'"));
        builder.ToTable("corporate_events", table =>
        {
            table.HasCheckConstraint("ck_corporate_events_kind", $"kind IN ({kinds})");
            table.HasCheckConstraint("ck_corporate_events_proportion", "from_quantity > 0 AND to_quantity > 0");
            table.HasCheckConstraint("ck_corporate_events_target",
                "(kind IN ('Conversion', 'Liquidation') AND target_asset_id IS NOT NULL AND target_asset_id <> asset_id) " +
                "OR (kind NOT IN ('Conversion', 'Liquidation') AND target_asset_id IS NULL)");
            // Liquidação (I.7c4): preço de referência maior que zero e caixa não negativo; nos outros tipos, nenhum dos dois.
            table.HasCheckConstraint("ck_corporate_events_liquidation",
                "(kind = 'Liquidation' AND target_unit_price > 0 AND (cash_per_unit IS NULL OR cash_per_unit >= 0)) " +
                "OR (kind <> 'Liquidation' AND target_unit_price IS NULL AND cash_per_unit IS NULL)");
        });

        builder.Property(e => e.Key).HasMaxLength(CorporateEvent.KeyMaxLength);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Date).HasColumnName("event_date");
        builder.Property(e => e.Source).HasMaxLength(500);

        builder.HasIndex(e => e.Key).IsUnique().HasDatabaseName(KeyIndex);
        builder.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(e => e.TargetAssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
