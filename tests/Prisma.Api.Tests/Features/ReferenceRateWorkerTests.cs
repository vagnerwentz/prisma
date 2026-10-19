using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prisma.Api.Features.Rates;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain;
using Prisma.Domain.Calendar;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// A agenda e a tarefa do CDI (docs/renda-fixa.md, D6): quando roda, que liga e desliga, que roda ao subir só se precisa e que
// uma falha nunca derruba a API. A sincronização em si está em ReferenceRateSyncTests.
public sealed class ReferenceRateWorkerTests
{
    // 19h30 e 23h30 em São Paulo (UTC−3, sem horário de verão desde 2019).
    private static readonly TimeOnly[] Times = [new(19, 30), new(23, 30)];

    private static DateTime SaoPaulo(int month, int day, int hour, int minute = 0) =>
        new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(3);

    // ---- Agenda ----

    [Theory]
    [InlineData(10, 19, 12, 0, 10, 19, 19, 30)] // meio-dia: hoje às 19h30
    [InlineData(10, 19, 19, 29, 10, 19, 19, 30)]
    [InlineData(10, 19, 19, 30, 10, 19, 23, 30)] // na hora exata: o próximo é o das 23h30 (acabou de rodar)
    [InlineData(10, 19, 20, 0, 10, 19, 23, 30)]
    [InlineData(10, 19, 23, 30, 10, 20, 19, 30)] // depois do último: amanhã às 19h30
    [InlineData(10, 19, 23, 59, 10, 20, 19, 30)]
    [InlineData(10, 20, 0, 5, 10, 20, 19, 30)] // madrugada
    [InlineData(10, 31, 23, 45, 11, 1, 19, 30)] // vira o mês
    [InlineData(12, 31, 23, 45, 1, 1, 19, 30)] // vira o ano (ver mês na expectativa abaixo)
    public void Next_run_is_the_next_scheduled_time_in_sao_paulo(
        int month, int day, int hour, int minute, int expectedMonth, int expectedDay, int expectedHour, int expectedMinute)
    {
        var now = SaoPaulo(month, day, hour, minute);
        var year = expectedMonth < month ? 2027 : 2026;

        var next = ReferenceRateSchedule.NextRun(now, Times);

        next.ShouldBe(new DateTime(year, expectedMonth, expectedDay, expectedHour, expectedMinute, 0, DateTimeKind.Utc).AddHours(3));
        next.Kind.ShouldBe(DateTimeKind.Utc);
    }

    // Sempre no futuro e nunca depois de um dia, para um instante a cada 13 minutos do ano inteiro (sem sorteio: varredura).
    [Fact]
    public void Next_run_is_always_in_the_future_within_a_day()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var minute = 0; minute <= 365 * 24 * 60; minute += 13)
        {
            var now = start.AddMinutes(minute);

            var next = ReferenceRateSchedule.NextRun(now, Times);

            (next > now && next - now <= TimeSpan.FromDays(1)).ShouldBeTrue($"a partir de {now:o} deu {next:o}");
        }
    }

    [Fact]
    public void The_order_of_the_configured_times_does_not_matter() =>
        ReferenceRateSchedule.NextRun(SaoPaulo(10, 19, 20), [new TimeOnly(23, 30), new TimeOnly(19, 30)])
            .ShouldBe(SaoPaulo(10, 19, 23, 30));

    [Fact]
    public void Next_run_requires_utc_and_at_least_one_time()
    {
        Should.Throw<ArgumentException>(() =>
            ReferenceRateSchedule.NextRun(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Local), Times));
        Should.Throw<ArgumentException>(() => ReferenceRateSchedule.NextRun(SaoPaulo(10, 19, 12), []));
    }

    // ---- Configuração ----

    [Fact]
    public void It_is_on_by_default() => HasWorker(Features()).ShouldBeTrue();

    [Fact]
    public void It_turns_off_by_configuration() => HasWorker(Features(("Rates:Sync:Enabled", "false"))).ShouldBeFalse();

    [Fact]
    public void The_defaults_are_the_approved_ones()
    {
        using var provider = Features().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RateSyncOptions>>().Value;

        options.ParsedRunTimes().ShouldBe(Times);
        options.OverlapDays.ShouldBe(10);
        options.InitialLoadFrom.ShouldBe(new DateOnly(2015, 1, 1));
    }

    [Theory]
    [InlineData("Rates:Sync:RunTimes:0", "25:00")]
    [InlineData("Rates:Sync:RunTimes:0", "19h30")]
    [InlineData("Rates:Sync:RunTimes:0", "")]
    [InlineData("Rates:Sync:OverlapDays", "-1")]
    [InlineData("Rates:Sync:OverlapDays", "61")]
    [InlineData("Rates:Sync:InitialLoadFrom", "1990-01-01")]
    public void Invalid_configuration_is_rejected(string key, string value)
    {
        using var provider = Features((key, value)).BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RateSyncOptions>>().Value);
    }

    // ---- A tarefa ----

    // O banco fora do ar: erro no log, e a tarefa segue.
    [Fact]
    public async Task Unexpected_failure_is_an_error_and_the_worker_keeps_going()
    {
        var logs = new LogSink();
        await using var provider = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton<IClock>(new FakeClock(new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc)))
            .AddSingleton(Options.Create(new RateSyncOptions()))
            .AddScoped<IReferenceRateSource>(_ => new FakeReferenceRateSource())
            .AddScoped<AppDbContext>(_ => throw new InvalidOperationException("banco fora do ar"))
            .AddSingleton<ReferenceRateSync>()
            .AddSingleton<ReferenceRateWorker>()
            .BuildServiceProvider();
        var worker = provider.GetRequiredService<ReferenceRateWorker>();

        await worker.StartAsync(CancellationToken.None);
        await logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");
        await worker.StopAsync(CancellationToken.None);

        logs.Entries.Single(e => e.EventId.Name == "ReferenceRateSyncFailed").Level.ShouldBe(LogLevel.Error);
        worker.ExecuteTask!.IsCompleted.ShouldBeTrue();
        worker.ExecuteTask.IsFaulted.ShouldBeFalse();
    }

    // ---- A tarefa ligada na API, contra o Postgres ----

    [Collection(ApiCollection.Name)]
    public sealed class OnStartup(PostgresFixture postgres)
    {
        private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

        private readonly FakeReferenceRateSource _source = new();
        private readonly FakeClock _clock = new(Monday);
        private readonly LogSink _logs = new();

        private void Publish(DateOnly from, DateOnly to) =>
            _source.Rates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(from.AddDays)
                .Where(BankCalendar.IsBusinessDay)
                .Select(date => new DailyRate(date, 50_000 + date.DayNumber % 1_000))
                .ToList();

        [Fact]
        public async Task With_nothing_stored_it_syncs_right_away_without_waiting_for_the_schedule()
        {
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16));
            await using var factory = await Factory(workerEnabled: true);

            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            _source.Calls.ShouldBe(1);
            (await Count(factory)).ShouldBe(_source.Rates.Count);
            _logs.Entries.ShouldContain(e => e.EventId.Name == "ReferenceRatesSynced");
        }

        [Fact]
        public async Task With_current_history_it_does_not_call_the_provider_on_startup()
        {
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16));
            await using (var seeding = await Factory(workerEnabled: false))
                await seeding.Services.GetRequiredService<ReferenceRateSync>().RunAsync(CancellationToken.None);
            var callsBefore = _source.Calls;

            await using var factory = await Factory(workerEnabled: true, clear: false);
            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            _source.Calls.ShouldBe(callsBefore);
        }

        [Fact]
        public async Task With_stale_history_it_syncs_on_startup()
        {
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 14));
            await using (var seeding = await Factory(workerEnabled: false))
                await seeding.Services.GetRequiredService<ReferenceRateSync>().RunAsync(CancellationToken.None);
            var callsBefore = _source.Calls;
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16)); // as de quinta e sexta saíram depois

            await using var factory = await Factory(workerEnabled: true, clear: false);
            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            _source.Calls.ShouldBe(callsBefore + 1);
            (await Count(factory)).ShouldBe(_source.Rates.Count);
        }

        // O Banco Central fora do ar na subida é aviso, não erro, e a API sobe do mesmo jeito.
        [Fact]
        public async Task The_provider_being_down_on_startup_is_a_warning_and_the_api_still_starts()
        {
            _source.Failure = new MarketDataException("bcb sgs 12 2015: HTTP 503");
            await using var factory = await Factory(workerEnabled: true);

            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            _logs.Entries.Single(e => e.EventId.Name == "ReferenceRateSyncUnavailable").Level.ShouldBe(LogLevel.Warning);
            _logs.Entries.ShouldNotContain(e => e.EventId.Name == "ReferenceRateSyncFailed");
            (await Count(factory)).ShouldBe(0);
            var response = await factory.CreateHttpsClient().GetAsync("/health");
            response.IsSuccessStatusCode.ShouldBeTrue();
        }

        // A tarefa grava cada execução para a tela de admin (docs/admin-mercado.md, 8): ao subir, com o tipo Startup.
        [Fact]
        public async Task The_startup_run_is_recorded_with_its_counts()
        {
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16));
            await using var factory = await Factory(workerEnabled: true);

            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            var run = await SingleRun(factory);
            (run.Kind, run.Outcome).ShouldBe((MarketSyncKind.Startup, MarketSyncOutcome.Succeeded));
            (run.Fetched, run.Added).ShouldBe((_source.Rates.Count, _source.Rates.Count));
            (run.WindowFrom, run.WindowTo).ShouldBe((new DateOnly(2015, 1, 1), new DateOnly(2026, 10, 19)));
            run.StartedAt.ShouldBe(Monday);
        }

        [Fact]
        public async Task A_provider_failure_on_startup_is_recorded_with_the_error_code()
        {
            _source.Failure = new MarketDataException("bcb sgs 12 2015: HTTP 503", sourceErrorCode: "555");
            await using var factory = await Factory(workerEnabled: true);

            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            var run = await SingleRun(factory);
            (run.Kind, run.Outcome, run.SourceErrorCode).ShouldBe((MarketSyncKind.Startup, MarketSyncOutcome.SourceUnavailable, "555"));
        }

        // Em dia ao subir, a tarefa não chama a fonte e não deixa execução gravada.
        [Fact]
        public async Task A_startup_that_does_nothing_records_nothing()
        {
            Publish(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 16));
            await using (var seeding = await Factory(workerEnabled: false))
                await seeding.Services.GetRequiredService<ReferenceRateSync>().RunAsync(CancellationToken.None);

            await using var factory = await Factory(workerEnabled: true, clear: false);
            _ = factory.Services;
            await _logs.WaitFor(e => e.EventId.Name == "ReferenceRateNextRun");

            await using var scope = factory.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MarketSyncRuns.CountAsync()).ShouldBe(0);
        }

        private static async Task<MarketSyncRun> SingleRun(PrismaApiFactory factory)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MarketSyncRuns.AsNoTracking().SingleAsync();
        }

        private static async Task<int> Count(PrismaApiFactory factory)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.CountAsync();
        }

        private async Task<PrismaApiFactory> Factory(bool workerEnabled, bool clear = true)
        {
            if (clear)
            {
                await using var cleaner = new PrismaApiFactory(postgres.ConnectionString);
                await using var scope = cleaner.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.ReferenceRates.ExecuteDeleteAsync();
                await db.MarketSyncRuns.ExecuteDeleteAsync();
            }

            return new PrismaApiFactory(
                postgres.ConnectionString,
                new Dictionary<string, string> { ["Rates:Sync:Enabled"] = workerEnabled ? "true" : "false" },
                _clock,
                _logs,
                services => services.AddScoped<IReferenceRateSource>(_ => _source));
        }
    }

    private static IServiceCollection Features(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ServiceCollection().AddReferenceRateFeatures(configuration);
    }

    private static bool HasWorker(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ReferenceRateWorker));
}
