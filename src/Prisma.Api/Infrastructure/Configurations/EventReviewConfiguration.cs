using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain.Investments;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Configurations;

// A conferência de um evento por uma pessoa (docs/investimentos.md, I.7c2).
public sealed class EventReviewConfiguration : IEntityTypeConfiguration<EventReview>
{
    public const string EventIndex = "ux_event_reviews_user_event";

    public void Configure(EntityTypeBuilder<EventReview> builder)
    {
        var verdicts = string.Join(", ", Enum.GetNames<EventReviewVerdict>().Select(v => $"'{v}'"));
        builder.ToTable("event_reviews", table =>
        {
            table.HasCheckConstraint("ck_event_reviews_verdict", $"verdict IN ({verdicts})");
            table.HasCheckConstraint("ck_event_reviews_reported_quantity",
                $"reported_quantity IS NULL OR (verdict = 'Disputed' AND reported_quantity BETWEEN 0 AND {EventReview.MaxReportedQuantity})");
        });

        builder.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        // Sair o evento do arquivo apaga as conferências dele: não há mais o que conferir.
        builder.HasOne<CorporateEvent>().WithMany().HasForeignKey(r => r.CorporateEventId).OnDelete(DeleteBehavior.Cascade);

        // Uma por evento por pessoa, contando as desfeitas: conferir de novo devolve a mesma, e dois toques ao mesmo tempo
        // não criam duas. O índice também serve à consulta da curadoria (os "Não confere" por evento).
        builder.HasIndex(r => new { r.UserId, r.CorporateEventId }).IsUnique().HasDatabaseName(EventIndex);
        builder.HasIndex(r => r.CorporateEventId);
    }
}
