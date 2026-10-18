using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Configurations;

// Taxas de referência (CDI diário), dado de mercado global (docs/renda-fixa.md, 2.4): sem user_id e sem filtro (CLAUDE.md,
// regra 6, exceção dos dados de mercado). AppDbContext.GlobalTables lista esta tabela.
public sealed class ReferenceRateConfiguration : IEntityTypeConfiguration<ReferenceRate>
{
    public const string SeriesDateIndex = "ux_reference_rates_series_date";

    public void Configure(EntityTypeBuilder<ReferenceRate> builder)
    {
        var series = string.Join(", ", Enum.GetNames<ReferenceRateSeries>().Select(s => $"'{s}'"));
        builder.ToTable("reference_rates", table =>
        {
            table.HasCheckConstraint("ck_reference_rates_series", $"series IN ({series})");
            // O mesmo limite do RateHistory: de 1 × 10⁻⁸ até menos de 1 % ao dia.
            table.HasCheckConstraint(
                "ck_reference_rates_rate", $"daily_rate_e8 >= 1 AND daily_rate_e8 < {RateHistory.MaxRateE8}");
        });

        builder.Property(r => r.Series).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Source).HasMaxLength(40);
        builder.Ignore(r => r.Rate);

        // Uma taxa por série por data: é o que torna a sincronização idempotente.
        builder.HasIndex(r => new { r.Series, r.ReferenceDate }).IsUnique().HasDatabaseName(SeriesDateIndex);
    }
}
