using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Investments;

namespace Prisma.Api.Infrastructure.Configurations;

public sealed class AssetRequestConfiguration : IEntityTypeConfiguration<AssetRequest>
{
    public const string SymbolIndex = "ux_asset_requests_user_symbol";

    public void Configure(EntityTypeBuilder<AssetRequest> builder)
    {
        builder.ToTable("asset_requests");

        builder.Property(r => r.Symbol).HasMaxLength(AssetRequest.SymbolMaxLength);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);

        // Sem chave estrangeira para assets: o pedido existe justamente porque o código não está lá. Um por código
        // por pessoa, contando os cancelados: avisar de novo devolve o mesmo, e dois toques ao mesmo tempo não criam
        // dois. O índice também serve à fila da curadoria, que agrupa por código.
        builder.HasIndex(r => new { r.UserId, r.Symbol }).IsUnique().HasDatabaseName(SymbolIndex);
        builder.HasIndex(r => r.Symbol);
    }
}
