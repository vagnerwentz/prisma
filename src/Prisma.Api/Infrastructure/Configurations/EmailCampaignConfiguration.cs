using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Infrastructure.Configurations;

// Disparos do admin (docs/admin-email.md, seção 4). Sem dono (AppDbContext.GlobalTables).
public sealed class EmailCampaignConfiguration : IEntityTypeConfiguration<EmailCampaign>
{
    public void Configure(EntityTypeBuilder<EmailCampaign> builder)
    {
        builder.ToTable("email_campaigns");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(c => c.ClientRequestId).IsUnique();
        builder.HasIndex(c => c.CreatedAt);
    }
}

public sealed class EmailCampaignRecipientConfiguration : IEntityTypeConfiguration<EmailCampaignRecipient>
{
    public void Configure(EntityTypeBuilder<EmailCampaignRecipient> builder)
    {
        builder.ToTable("email_campaign_recipients");
        builder.HasKey(r => new { r.CampaignId, r.UserId });
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(30);

        builder.HasOne<EmailCampaign>().WithMany().HasForeignKey(r => r.CampaignId).OnDelete(DeleteBehavior.Cascade);
        // A conta apagada leva junto o registro de que recebeu o disparo (AccountDeletionTests confere a cascata).
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        // "Esta pessoa já recebeu este modelo (e quando)?": o filtro do público de cada disparo.
        builder.HasIndex(r => new { r.UserId, r.Kind, r.CreatedAt });
    }
}
