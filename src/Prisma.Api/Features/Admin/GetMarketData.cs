using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Features.Rates;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.FixedIncome.Coverage;

namespace Prisma.Api.Features.Admin;

// O estado de cada série de mercado (docs/admin-mercado.md, 4): até onde vai o histórico, se está em dia ou incompleto, quando a
// tarefa roda de novo e como foram as últimas execuções. Só lê dado global.
public static class GetMarketData
{
    public sealed class Handler(AppDbContext db, IClock clock, IOptions<RateSyncOptions> rates)
    {
        public async Task<IReadOnlyList<MarketSeriesStatus>> Execute(CancellationToken ct)
        {
            var statuses = new List<MarketSeriesStatus>();
            foreach (var series in MarketSeriesCatalog.All)
                statuses.Add(await StatusOf(series, ct));
            return statuses;
        }

        private async Task<MarketSeriesStatus> StatusOf(MarketSeries series, CancellationToken ct)
        {
            var stored = db.ReferenceRates.AsNoTracking().Where(r => r.Series == series.Series);
            var dates = await stored.Select(r => r.ReferenceDate).ToListAsync(ct);
            var coverage = ReferenceRateCoverage.Analyze(dates, clock.Today);
            var lastFetchedAt = await stored.MaxAsync(r => (DateTime?)r.FetchedAt, ct);
            var lastRevisedAt = await stored.MaxAsync(r => r.RevisedAt, ct);

            var runs = db.MarketSyncRuns.AsNoTracking().Where(r => r.Series == series.Series).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id);
            var last = await runs.FirstOrDefaultAsync(ct);
            var lastSucceeded = await runs.Where(r => r.Outcome == MarketSyncOutcome.Succeeded).FirstOrDefaultAsync(ct);

            var settings = rates.Value;
            var schedule = new MarketSyncSchedule(
                settings.ParsedRunTimes().Order().Select(t => t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                ReferenceRateSchedule.NextRun(clock.UtcNow, settings.ParsedRunTimes()),
                settings.Enabled);

            return new MarketSeriesStatus(
                series.Code, series.Name, series.Source, dates.Count, coverage.First, coverage.Last, coverage.ExpectedLast, coverage.IsStale,
                coverage.IsComplete, coverage.MissingBusinessDays.Count, lastFetchedAt, lastRevisedAt, schedule,
                last is null ? null : MarketSyncRunResponse.Of(last), lastSucceeded is null ? null : MarketSyncRunResponse.Of(lastSucceeded));
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<MarketSeriesStatus>>(200)
            .ProducesProblem(401)
            .ProducesProblem(403);
}
