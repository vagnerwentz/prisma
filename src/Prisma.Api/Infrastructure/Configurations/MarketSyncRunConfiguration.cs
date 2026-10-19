using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Configurations;

// Execuções da sincronização dos dados de mercado (docs/admin-mercado.md, 5): dado operacional global, sem user_id e sem filtro
// (CLAUDE.md, regra 6, exceção dos dados de mercado). AppDbContext.GlobalTables lista esta tabela. Não guarda quem pediu.
public sealed class MarketSyncRunConfiguration : IEntityTypeConfiguration<MarketSyncRun>
{
    public const string SeriesStartedIndex = "ix_market_sync_runs_series_started_at";

    public void Configure(EntityTypeBuilder<MarketSyncRun> builder)
    {
        static string Names<T>() where T : struct, Enum => string.Join(", ", Enum.GetNames<T>().Select(n => $"'{n}'"));

        builder.ToTable("market_sync_runs", table =>
        {
            table.HasCheckConstraint("ck_market_sync_runs_series", $"series IN ({Names<ReferenceRateSeries>()})");
            table.HasCheckConstraint("ck_market_sync_runs_kind", $"kind IN ({Names<MarketSyncKind>()})");
            table.HasCheckConstraint("ck_market_sync_runs_outcome", $"outcome IN ({Names<MarketSyncOutcome>()})");
            table.HasCheckConstraint("ck_market_sync_runs_window", "window_from <= window_to");
            table.HasCheckConstraint("ck_market_sync_runs_counts", "fetched >= 0 AND added >= 0 AND revised >= 0 AND unchanged >= 0");
        });

        builder.Property(r => r.Series).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SourceErrorCode).HasMaxLength(MarketSyncRun.MaxSourceErrorCodeLength);
        builder.Property(r => r.ErrorSummary).HasMaxLength(MarketSyncRun.MaxErrorSummaryLength);

        builder.HasIndex(r => new { r.Series, r.StartedAt }).IsDescending(false, true).HasDatabaseName(SeriesStartedIndex);
    }
}
