using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

// Logo de cada ativo, já limpo (docs/investimentos.md, etapa 4). Global, como o catálogo (AppDbContext.GlobalTables).
// Tabela à parte: a sincronização diária carrega os ativos sem carregar os desenhos.
public sealed class AssetLogoConfiguration : IEntityTypeConfiguration<AssetLogo>
{
    public void Configure(EntityTypeBuilder<AssetLogo> builder)
    {
        builder.ToTable("asset_logos");
        builder.HasKey(l => l.AssetId);
        builder.HasOne<Asset>().WithOne().HasForeignKey<AssetLogo>(l => l.AssetId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(l => l.SourceUrl).HasMaxLength(500);
    }
}
