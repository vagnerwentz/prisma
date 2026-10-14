using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Infrastructure.Configurations;

// Fila de e-mails (docs/email.md, seção 3). Sem dono (AppDbContext.GlobalTables).
public sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.ToTable("outbox_emails");
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ToAddress).HasMaxLength(OutboxEmail.AddressMaxLength);
        builder.Property(e => e.LastError).HasMaxLength(OutboxEmail.ErrorMaxLength);
        builder.Property(e => e.ProviderMessageId).HasMaxLength(OutboxEmail.ProviderIdMaxLength);

        // A conta apagada leva junto o que ainda ia para ela.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

        // A reserva do lote olha só os pendentes; os enviados ficam fora do índice.
        builder.HasIndex(e => e.NextAttemptAt).HasFilter("status = 'Pending'");
        // O limite por pessoa e por tipo (EmailQueue).
        builder.HasIndex(e => new { e.UserId, e.Kind, e.CreatedAt });
    }
}
