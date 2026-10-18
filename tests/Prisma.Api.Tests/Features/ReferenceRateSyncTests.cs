using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Rates;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Calendar;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// A sincronização do CDI contra o Postgres das migrations reais, com o Banco Central falso (docs/renda-fixa.md, 3.2 e 3.3):
// idempotência, revisão, falha sem efeito, duas execuções ao mesmo tempo, lacuna e defasagem. A tabela é global, então cada
// teste começa com ela vazia.
[Collection(ApiCollection.Name)]
public sealed class ReferenceRateSyncTests(PostgresFixture postgres)
{
    // Segunda-feira, 12h em São Paulo. A última taxa publicável é a de sexta, 16/10 (o CDI de um dia sai à noite).
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateOnly LastPublished = new(2026, 10, 16);
    private static readonly DateOnly FirstDay = new(2026, 9, 1);

    private readonly FakeReferenceRateSource _source = new();
    private readonly FakeClock _clock = new(Monday);
    private readonly LogSink _logs = new();

    // Uma taxa por dia útil, de 01/09 a 16/10/2026, com valor que muda de dia para dia.
    private static List<DailyRate> BusinessDayRates(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Where(BankCalendar.IsBusinessDay)
            .Select(date => new DailyRate(date, 50_000 + date.DayNumber % 1_000))
            .ToList();

    private void Publish(DateOnly from, DateOnly to) => _source.Rates = BusinessDayRates(from, to);

    // ---- Janelas ----

    [Fact]
    public async Task The_first_sync_loads_the_history_from_the_configured_start()
    {
        await using var factory = await Factory();
        Publish(new DateOnly(2026, 1, 2), LastPublished);

        var result = await Sync(factory);

        _source.Requests.ShouldBe([(new DateOnly(2015, 1, 1), Today)]);
        var rows = await Stored(factory);
        rows.Count.ShouldBe(result.Added);
        result.Added.ShouldBe(_source.Rates.Count);
        rows.ShouldAllBe(r => r.Series == ReferenceRateSeries.Cdi && r.Source == "bcb-sgs-12" && r.FetchedAt == Monday);
        rows.Select(r => r.Rate).ShouldBe(_source.Rates);
    }

    [Fact]
    public async Task The_next_syncs_ask_from_ten_days_before_the_last_stored_date()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        await Sync(factory);

        await Sync(factory);

        _source.Requests.Last().ShouldBe((LastPublished.AddDays(-10), Today));
    }

    // ---- Idempotência ----

    [Fact]
    public async Task Running_again_without_news_changes_nothing()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        await Sync(factory);
        var before = await Stored(factory);
        _clock.UtcNow = Monday.AddDays(1);

        var again = await Sync(factory);

        (again.Added, again.Revised).ShouldBe((0, 0));
        again.Unchanged.ShouldBeGreaterThan(0);
        var after = await Stored(factory);
        // Ordena só pelo Id: a tupla inteira não serve de chave, porque a taxa não é comparável e a ordenação, ao comparar um
        // elemento com ele mesmo, passaria por ela (falhava de vez em quando, conforme a ordem dos Ids).
        after.Select(r => (r.Id, r.Rate, r.FetchedAt, r.RevisedAt)).OrderBy(r => r.Id)
            .ShouldBe(before.Select(r => (r.Id, r.Rate, r.FetchedAt, r.RevisedAt)).OrderBy(r => r.Id));
    }

    [Fact]
    public async Task A_new_published_day_is_added_and_the_rest_is_left_alone()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished.AddDays(-2)); // até quarta, 14/10
        await Sync(factory);
        var before = await Stored(factory);

        _clock.UtcNow = Monday.AddDays(1);
        Publish(FirstDay, LastPublished); // saiu a de quinta e a de sexta
        var result = await Sync(factory);

        result.Added.ShouldBe(2);
        var after = await Stored(factory);
        after.Count.ShouldBe(before.Count + 2);
        after.Where(r => before.Any(b => b.Id == r.Id)).ShouldAllBe(r => r.FetchedAt == Monday);
    }

    // Fim de semana: a API não tem dia útil na faixa e responde "sem valores". A sincronização termina sem erro e sem mexer em nada.
    [Fact]
    public async Task A_range_with_no_news_is_not_an_error()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        await Sync(factory);
        _source.Rates = [];

        var result = await Sync(factory);

        (result.Fetched, result.Added, result.Revised, result.Unchanged).ShouldBe((0, 0, 0, 0));
        (await Stored(factory)).Count.ShouldBe(BusinessDayRates(FirstDay, LastPublished).Count);
    }

    // ---- Revisão ----

    [Fact]
    public async Task A_corrected_value_is_updated_noted_and_logged_never_silent()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        await Sync(factory);
        var revisedDate = new DateOnly(2026, 10, 14);
        _clock.UtcNow = Monday.AddDays(1);
        _source.Rates = _source.Rates.Select(r => r.Date == revisedDate ? r with { RateE8 = r.RateE8 + 7 } : r).ToList();
        var expected = _source.Rates.Single(r => r.Date == revisedDate);

        var result = await Sync(factory);

        result.Revised.ShouldBe(1);
        var rows = await Stored(factory);
        var revised = rows.Single(r => r.ReferenceDate == revisedDate);
        revised.Rate.ShouldBe(expected);
        revised.RevisedAt.ShouldBe(Monday.AddDays(1));
        revised.FetchedAt.ShouldBe(Monday.AddDays(1));
        rows.Where(r => r.Id != revised.Id).ShouldAllBe(r => r.RevisedAt == null && r.FetchedAt == Monday);
        var warning = _logs.Entries.Single(e => e.EventId.Name == "ReferenceRateRevised");
        warning.Level.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Warning);
        warning["ReferenceDate"].ShouldBe(revisedDate);
    }

    // ---- Falha ----

    [Fact]
    public async Task A_provider_failure_leaves_the_table_as_it_was_and_the_next_run_recovers()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished.AddDays(-2)); // até quarta, 14/10
        await Sync(factory);
        var before = await Stored(factory);
        var syncsBefore = _logs.Entries.Count(e => e.EventId.Name == "ReferenceRatesSynced");

        Publish(FirstDay, LastPublished);
        _source.Failure = new MarketDataException("bcb sgs 12: HTTP 502");
        await Should.ThrowAsync<MarketDataException>(() => Sync(factory));

        (await Stored(factory)).Count.ShouldBe(before.Count);
        _logs.Entries.Count(e => e.EventId.Name == "ReferenceRatesSynced").ShouldBe(syncsBefore);

        _source.Failure = null;
        (await Sync(factory)).Added.ShouldBe(2);
    }

    [Fact]
    public async Task A_failure_on_the_very_first_sync_leaves_the_table_empty()
    {
        await using var factory = await Factory();
        _source.Failure = new MarketDataException("bcb sgs 12 2015: timed out");

        await Should.ThrowAsync<MarketDataException>(() => Sync(factory));

        (await Stored(factory)).ShouldBeEmpty();
    }

    // ---- Duas execuções ao mesmo tempo ----

    // A garantia final é o índice único, não o código: várias execuções juntas deixam uma linha por data e nenhuma explode.
    [Fact]
    public async Task Several_syncs_at_the_same_time_leave_one_row_per_date()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        var sync = factory.Services.GetRequiredService<ReferenceRateSync>();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => sync.RunAsync(CancellationToken.None)));

        var rows = await Stored(factory);
        rows.Count.ShouldBe(_source.Rates.Count);
        rows.Select(r => r.ReferenceDate).Distinct().Count().ShouldBe(rows.Count);
        results.Sum(r => r.Added).ShouldBe(_source.Rates.Count);
    }

    // O caminho do índice único, sem depender de sorte: outra execução grava uma das datas no meio da busca.
    [Fact]
    public async Task A_row_written_by_another_run_in_the_middle_is_not_a_failure()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        var rival = _source.Rates[10];
        _source.OnFetch = async () =>
        {
            _source.OnFetch = null;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ReferenceRates.Add(ReferenceRate.From(ReferenceRateSeries.Cdi, rival, "bcb-sgs-12", Monday));
            await db.SaveChangesAsync();
        };

        var result = await Sync(factory);

        (await Stored(factory)).Count.ShouldBe(_source.Rates.Count);
        (result.Added, result.Unchanged).ShouldBe((_source.Rates.Count - 1, 1));
        _source.Calls.ShouldBe(1); // a busca não se repete na segunda tentativa
    }

    // ---- Lacuna e defasagem ----

    [Fact]
    public async Task A_missing_business_day_is_reported_as_a_gap()
    {
        await using var factory = await Factory();
        var hole = new DateOnly(2026, 9, 15);
        Publish(FirstDay, LastPublished);
        _source.Rates = _source.Rates.Where(r => r.Date != hole).ToList();

        var result = await Sync(factory);

        result.Coverage.MissingBusinessDays.ShouldBe([hole]);
        var warning = _logs.Entries.Single(e => e.EventId.Name == "ReferenceRateGapDetected");
        (warning["Missing"], warning["FirstMissing"], warning["LastMissing"]).ShouldBe((1, hole, hole));
    }

    // Reprocessar o período conserta: é a mesma sincronização com outra janela, e é idempotente.
    [Fact]
    public async Task Refreshing_the_range_fills_the_gap()
    {
        await using var factory = await Factory();
        var hole = new DateOnly(2026, 9, 15);
        Publish(FirstDay, LastPublished);
        var complete = _source.Rates;
        _source.Rates = complete.Where(r => r.Date != hole).ToList();
        await Sync(factory);

        _source.Rates = complete;
        var result = await factory.Services.GetRequiredService<ReferenceRateSync>().RefreshRangeAsync(FirstDay, Today, CancellationToken.None);

        result.Added.ShouldBe(1);
        result.Coverage.IsComplete.ShouldBeTrue();
        (await Stored(factory)).Count.ShouldBe(complete.Count);
        _source.Requests.Last().ShouldBe((FirstDay, Today));
    }

    [Fact]
    public async Task Fresh_and_complete_history_logs_no_warning()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);

        var result = await Sync(factory);

        result.Coverage.IsStale.ShouldBeFalse();
        result.Coverage.IsComplete.ShouldBeTrue();
        var warnings = new[] { "ReferenceRateGapDetected", "ReferenceRatesStale", "ReferenceRateRevised" };
        _logs.Entries.Select(e => e.EventId.Name).Intersect(warnings).ShouldBeEmpty();
    }

    // Quarta-feira de manhã a taxa de terça deveria estar aqui. A fonte não trouxe: defasada, e o log diz até quando vai.
    [Fact]
    public async Task A_history_older_than_the_last_business_day_is_reported_as_stale()
    {
        await using var factory = await Factory();
        Publish(FirstDay, LastPublished);
        _clock.UtcNow = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc); // quarta; o esperado é terça, 20/10

        var result = await Sync(factory);

        result.Coverage.IsStale.ShouldBeTrue();
        var warning = _logs.Entries.Single(e => e.EventId.Name == "ReferenceRatesStale");
        (warning["LastStored"], warning["ExpectedLast"]).ShouldBe((LastPublished, new DateOnly(2026, 10, 20)));
    }

    // ---- Quando rodar ao subir ----

    [Fact]
    public async Task It_needs_to_run_when_empty_or_stale_and_not_when_current()
    {
        await using var factory = await Factory();
        var sync = factory.Services.GetRequiredService<ReferenceRateSync>();
        Publish(FirstDay, LastPublished);

        (await sync.NeedsRunAsync(CancellationToken.None)).ShouldBeTrue(); // vazia
        await Sync(factory);
        (await sync.NeedsRunAsync(CancellationToken.None)).ShouldBeFalse(); // em dia na segunda
        _clock.UtcNow = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
        (await sync.NeedsRunAsync(CancellationToken.None)).ShouldBeTrue(); // quarta: falta a de terça
    }

    // ---- Apoio ----

    private async Task<ReferenceRateSyncResult> Sync(PrismaApiFactory factory) =>
        await factory.Services.GetRequiredService<ReferenceRateSync>().RunAsync(CancellationToken.None);

    private static async Task<List<ReferenceRate>> Stored(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.AsNoTracking().OrderBy(r => r.ReferenceDate).ToListAsync();
    }

    private async Task<PrismaApiFactory> Factory()
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" },
            _clock,
            _logs,
            services => services.AddScoped<IReferenceRateSource>(_ => _source));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();
        return factory;
    }
}
