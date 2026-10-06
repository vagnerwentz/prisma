using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

// Catálogo global de ativos (docs/investimentos.md, seção 3): sem user_id e sem filtro (CLAUDE.md, regra 6,
// exceção dos dados de mercado). AppDbContext.GlobalTables lista esta tabela.
public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public const string SymbolIndex = "ux_assets_symbol";

    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        var kinds = string.Join(", ", Enum.GetNames<AssetKind>().Select(k => $"'{k}'"));
        builder.ToTable("assets", table =>
        {
            table.HasCheckConstraint("ck_assets_kind", $"kind IN ({kinds})");
            table.HasCheckConstraint("ck_assets_symbol", "symbol ~ '^[A-Z0-9]+$'");
        });

        builder.Property(a => a.Symbol).HasMaxLength(ListedAsset.MaxSymbolLength);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.LogoUrl).HasMaxLength(500);
        builder.Ignore(a => a.IsActive);
        builder.Ignore(a => a.DisplayName);

        builder.HasIndex(a => a.Symbol).IsUnique().HasDatabaseName(SymbolIndex);
    }
}
