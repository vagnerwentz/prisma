using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Investments;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public const string AssetIndex = "ux_holdings_user_asset";

    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("holdings");

        builder.HasOne<AppUser>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(h => h.AssetId).OnDelete(DeleteBehavior.Restrict);

        // Um por ativo por pessoa, contando o que foi tirado da carteira: pôr de novo devolve o mesmo registro,
        // e dois toques em "Adicionar" ao mesmo tempo não criam dois.
        builder.HasIndex(h => new { h.UserId, h.AssetId }).IsUnique().HasDatabaseName(AssetIndex);
    }
}
