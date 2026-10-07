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
                "(kind = 'Conversion' AND target_asset_id IS NOT NULL AND target_asset_id <> asset_id) OR (kind <> 'Conversion' AND target_asset_id IS NULL)");
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
