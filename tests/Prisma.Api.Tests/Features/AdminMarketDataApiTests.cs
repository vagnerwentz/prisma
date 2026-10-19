using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Prisma.Api.Features.Rates;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Calendar;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// A superfície de admin dos dados de mercado (docs/admin-mercado.md, seção 9): quem entra, o que dispara, o que fica gravado.
// Banco Central falso, Postgres das migrations reais. As tabelas são globais, então cada teste começa com elas vazias.
[Collection(ApiCollection.Name)]
public sealed class AdminMarketDataApiTests(PostgresFixture postgres)
{
    // Segunda-feira, 12h em São Paulo. A última taxa publicável é a de sexta, 16/10.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LastPublished = new(2026, 10, 16);
    private static readonly DateOnly FirstDay = new(2026, 9, 1);

    private readonly FakeReferenceRateSource _source = new();
    private readonly FakeClock _clock = new(Monday);
    private readonly LogSink _logs = new();
    private readonly string _adminEmail = $"admin-{Guid.NewGuid():N}@teste.com.br";

    private sealed record RunDto(
        Guid Id, string Series, string Kind, DateTime StartedAt, DateTime FinishedAt, string Outcome, DateOnly WindowFrom, DateOnly WindowTo,
        int Fetched, int Added, int Revised, int Unchanged, string? SourceErrorCode, string? ErrorSummary);

    private sealed record ScheduleDto(List<string> RunTimes, DateTime NextRunUtc, bool Enabled);

    private sealed record StatusDto(
        string Code, string Name, string Source, int Count, DateOnly? First, DateOnly? Last, DateOnly ExpectedLast, bool Stale, bool Complete,
        int MissingBusinessDayCount, DateTime? LastFetchedAt, DateTime? LastRevisedAt, ScheduleDto Schedule, RunDto? LastRun, RunDto? LastSuccessfulRun);

    private sealed record MeDto(Guid Id, string Email, bool MustAcceptTerms, bool IsAdmin);

    private void Publish(DateOnly from, DateOnly to) =>
        _source.Rates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Where(BankCalendar.IsBusinessDay)
            .Select(date => new DailyRate(date, 50_000 + date.DayNumber % 1_000))
            .ToList();

    private async Task<PrismaApiFactory> Factory(string? adminEmail = null, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string>
        {
            ["Rates:Sync:Enabled"] = "false",
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["RateLimiting:AssetRequests:PermitLimit"] = "1000",
            ["RateLimiting:AdminSync:PermitLimit"] = "1000",
        };
        if (adminEmail is not null)
            settings["Admin:Emails:0"] = adminEmail;
        foreach (var (key, value) in extra)
            settings[key] = value;

        var factory = new PrismaApiFactory(
            postgres.ConnectionString, settings, _clock, _logs, services => services.AddScoped<IReferenceRateSource>(_ => _source));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ReferenceRates.ExecuteDeleteAsync();
        await db.MarketSyncRuns.ExecuteDeleteAsync();
        return factory;
    }

    private async Task<PrismaApiFactory> AdminFactory(params (string Key, string Value)[] extra) => await Factory(_adminEmail, extra);

    private static async Task<HttpClient> SignIn(PrismaApiFactory factory, string email)
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();
        return client;
    }

    private Task<HttpClient> Admin(PrismaApiFactory factory) => SignIn(factory, _adminEmail);

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<List<MarketSyncRun>> Runs(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MarketSyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync();
    }

    private static async Task<int> RateCount(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.CountAsync();
    }

    // ---- 1. Autorização ----

    [Fact]
    public async Task An_anonymous_caller_gets_401_on_every_admin_route()
    {
        await using var factory = await AdminFactory();
        var client = factory.CreateHttpsClient();

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/admin/market-data/cdi/sync", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2026-10-01", to = "2026-10-16" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/admin/market-data/cdi/runs")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_regular_user_gets_403_and_nothing_runs()
    {
        await using var factory = await AdminFactory();
        var client = await factory.CreateAuthenticatedClientAsync();
        Publish(FirstDay, LastPublished);

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsync("/admin/market-data/cdi/sync", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2026-10-01", to = "2026-10-16" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/admin/market-data/cdi/runs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _source.Calls.ShouldBe(0);
        (await Runs(factory)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_admin_gets_200()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/admin/market-data/cdi/runs")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Sem a configuração, ninguém é admin: nem o e-mail "óbvio".
    [Fact]
    public async Task Without_a_configured_list_nobody_is_admin()
    {
        await using var factory = await Factory(null);
        var client = await SignIn(factory, _adminEmail);

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_email_matches_regardless_of_letter_case()
    {
        await using var factory = await Factory(_adminEmail.ToUpperInvariant());
        var client = await SignIn(factory, _adminEmail);

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_email_that_is_not_confirmed_is_not_admin()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(_adminEmail))!;
            user.EmailConfirmed = false;
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Tirar o e-mail da lista tira o acesso na hora, sem esperar a sessão vencer.
    [Fact]
    public async Task Access_follows_the_configuration_on_every_request()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var other = await Factory(null);
        var sameUser = await SignIn(other, $"outro-{Guid.NewGuid():N}@teste.com.br");

        (await sameUser.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Falha fechada e alta: uma lista com lixo (ou entrada vazia) não vira "ninguém é admin" em silêncio, a API não sobe.
    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a b@teste.com")]
    [InlineData("")]
    public async Task A_list_with_something_that_is_not_an_email_stops_the_api_from_starting(string entry) =>
        await Should.ThrowAsync<OptionsValidationException>(() => Factory(entry));

    [Fact]
    public void The_options_validate_the_list_and_the_timeout()
    {
        new AdminOptions().IsValid().ShouldBeTrue();
        new AdminOptions { Emails = ["voce@exemplo.com"] }.IsValid().ShouldBeTrue();
        new AdminOptions { Emails = ["voce@exemplo.com", " "] }.IsValid().ShouldBeFalse();
        new AdminOptions { SyncTimeoutSeconds = 0 }.IsValid().ShouldBeFalse();
        new AdminOptions { SyncTimeoutSeconds = 301 }.IsValid().ShouldBeFalse();
    }

    // ---- 2. /auth/me ----

    [Fact]
    public async Task Me_says_who_is_admin_and_keeps_the_old_fields()
    {
        await using var factory = await AdminFactory();
        var admin = await Admin(factory);
        var regular = await factory.CreateAuthenticatedClientAsync();

        var adminMe = (await admin.GetFromJsonAsync<MeDto>("/auth/me"))!;
        var regularMe = (await regular.GetFromJsonAsync<MeDto>("/auth/me"))!;

        adminMe.IsAdmin.ShouldBeTrue();
        adminMe.Email.ShouldBe(_adminEmail);
        adminMe.MustAcceptTerms.ShouldBeFalse();
        adminMe.Id.ShouldNotBe(Guid.Empty);
        regularMe.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task Login_also_says_it()
    {
        await using var factory = await AdminFactory();
        await Admin(factory);

        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/auth/login", new { email = _adminEmail, password = PrismaApiFactory.Password });

        (await response.Content.ReadFromJsonAsync<MeDto>())!.IsAdmin.ShouldBeTrue();
    }

    // ---- 3. sync ----

    [Fact]
    public async Task Sync_stores_the_rates_returns_the_run_and_records_it()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);

        var response = await client.PostAsync("/admin/market-data/cdi/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var run = (await response.Content.ReadFromJsonAsync<RunDto>())!;
        run.Series.ShouldBe("cdi");
        run.Kind.ShouldBe("Manual");
        run.Outcome.ShouldBe("Succeeded");
        (run.Fetched, run.Added, run.Revised, run.Unchanged).ShouldBe((_source.Rates.Count, _source.Rates.Count, 0, 0));
        (run.WindowFrom, run.WindowTo).ShouldBe((new DateOnly(2015, 1, 1), new DateOnly(2026, 10, 19)));
        run.ErrorSummary.ShouldBeNull();
        (await RateCount(factory)).ShouldBe(_source.Rates.Count);

        var stored = (await Runs(factory)).Single();
        stored.Id.ShouldBe(run.Id);
        stored.Kind.ShouldBe(MarketSyncKind.Manual);
    }

    [Fact]
    public async Task Syncing_again_adds_nothing_and_asks_the_normal_window()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        await client.PostAsync("/admin/market-data/cdi/sync", null);

        var second = (await (await client.PostAsync("/admin/market-data/cdi/sync", null)).Content.ReadFromJsonAsync<RunDto>())!;

        second.Added.ShouldBe(0);
        second.Unchanged.ShouldBeGreaterThan(0);
        (second.WindowFrom, second.WindowTo).ShouldBe((LastPublished.AddDays(-10), new DateOnly(2026, 10, 19)));
        (await Runs(factory)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_source_gives_502_with_the_code_and_stores_no_rate()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        _source.Failure = new MarketDataException("bcb sgs 12: unexpected content type 'text/html'", sourceErrorCode: "7240913300019");

        var response = await client.PostAsync("/admin/market-data/cdi/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        using var problem = await Json(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("SourceUnavailable");
        problem.RootElement.GetProperty("sourceErrorCode").GetString().ShouldBe("7240913300019");
        problem.RootElement.GetProperty("detail").GetString().ShouldBe("O Banco Central não respondeu como esperado.");
        problem.RootElement.GetProperty("run").GetProperty("outcome").GetString().ShouldBe("SourceUnavailable");

        (await RateCount(factory)).ShouldBe(0);
        var stored = (await Runs(factory)).Single();
        stored.Outcome.ShouldBe(MarketSyncOutcome.SourceUnavailable);
        stored.SourceErrorCode.ShouldBe("7240913300019");
        stored.Kind.ShouldBe(MarketSyncKind.Manual);
    }

    // O corpo da exceção nunca chega ao banco nem à resposta: só o texto fixo e o código.
    [Fact]
    public async Task The_exception_message_never_reaches_the_response_or_the_table()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        _source.Failure = new MarketDataException("SEGREDO-DA-FONTE <html>", sourceErrorCode: "123");

        var response = await client.PostAsync("/admin/market-data/cdi/sync", null);

        (await response.Content.ReadAsStringAsync()).ShouldNotContain("SEGREDO-DA-FONTE");
        (await Runs(factory)).Single().ErrorSummary!.ShouldNotContain("SEGREDO-DA-FONTE");
        _logs.Entries.ShouldNotContain(e => e.Message.Contains("SEGREDO-DA-FONTE") && e.EventId.Name == "MarketDataSyncFinished");
    }

    [Fact]
    public async Task An_unexpected_failure_gives_500_and_is_recorded_as_failed()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        _source.Failure = new InvalidOperationException("quebrou");

        var response = await client.PostAsync("/admin/market-data/cdi/sync", null);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        using var problem = await Json(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("SyncFailed");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("quebrou");
        (await Runs(factory)).Single().Outcome.ShouldBe(MarketSyncOutcome.Failed);
    }

    // ---- 4. refresh ----

    [Fact]
    public async Task Refresh_reprocesses_the_window_asked_and_records_it()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);

        var response = await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2026-10-01", to = "2026-10-16" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var run = (await response.Content.ReadFromJsonAsync<RunDto>())!;
        (run.WindowFrom, run.WindowTo).ShouldBe((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16)));
        _source.Requests.Single().ShouldBe((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16)));
        run.Added.ShouldBe(_source.Rates.Count(r => r.Date >= new DateOnly(2026, 10, 1)));
        (await Runs(factory)).Single().Kind.ShouldBe(MarketSyncKind.Manual);
    }

    [Fact]
    public async Task Refresh_with_exactly_ten_years_is_accepted()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        var response = await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2016-10-19", to = "2026-10-19" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-09")] // início depois do fim
    [InlineData("2026-10-01", "2026-10-20")] // fim no futuro
    [InlineData("2014-12-31", "2015-01-31")] // antes do início do histórico
    [InlineData("2016-10-18", "2026-10-19")] // 10 anos e um dia
    public async Task An_invalid_window_gives_400_and_never_reaches_the_provider(string from, string to)
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        var response = await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from, to });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = await Json(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("InvalidWindow");
        _source.Calls.ShouldBe(0);
        (await Runs(factory)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refresh_without_a_valid_body_is_a_400()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        (await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "ontem", to = "hoje" })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        _source.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("selic")]
    [InlineData("CDI")]
    [InlineData("12")]
    public async Task An_unknown_series_gives_404_on_every_route(string series)
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        var sync = await client.PostAsync($"/admin/market-data/{series}/sync", null);
        var refresh = await client.PostAsJsonAsync($"/admin/market-data/{series}/refresh", new { from = "2026-10-01", to = "2026-10-16" });
        var runs = await client.GetAsync($"/admin/market-data/{series}/runs");

        foreach (var response in new[] { sync, refresh, runs })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using var problem = await Json(response);
            problem.RootElement.GetProperty("code").GetString().ShouldBe("UnknownSeries");
        }

        _source.Calls.ShouldBe(0);
    }

    // ---- 5. Corrida ----

    // A tarefa e dois disparos manuais ao mesmo tempo: o lock serializa e o índice único garante. Nenhuma taxa dobra, e todas
    // as execuções dão certo (a segunda espera a vez e já encontra o trabalho feito).
    [Fact]
    public async Task A_manual_sync_together_with_the_worker_does_not_duplicate_anything()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        _source.Delay = TimeSpan.FromMilliseconds(200);
        var sync = factory.Services.GetRequiredService<ReferenceRateSync>();

        var first = client.PostAsync("/admin/market-data/cdi/sync", null);
        var second = client.PostAsync("/admin/market-data/cdi/sync", null);
        var worker = sync.RunRecordedAsync(MarketSyncKind.Scheduled, null, null, null, CancellationToken.None);
        await Task.WhenAll(first, second, worker);

        (await first).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RateCount(factory)).ShouldBe(_source.Rates.Count);
        var runs = await Runs(factory);
        runs.Count.ShouldBe(3);
        runs.ShouldAllBe(r => r.Outcome == MarketSyncOutcome.Succeeded);
        runs.Sum(r => r.Added).ShouldBe(_source.Rates.Count);
    }

    // O lock do Postgres é o que serializa as execuções: a busca na fonte (feita dentro dele) nunca acontece em duas ao mesmo tempo.
    [Fact]
    public async Task Syncs_never_fetch_from_the_provider_at_the_same_time()
    {
        await using var factory = await AdminFactory();
        Publish(FirstDay, LastPublished);
        var inFlight = 0;
        var most = 0;
        _source.OnFetch = async () =>
        {
            most = Math.Max(most, Interlocked.Increment(ref inFlight));
            await Task.Delay(150);
            Interlocked.Decrement(ref inFlight);
        };
        var sync = factory.Services.GetRequiredService<ReferenceRateSync>();

        var runs = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            sync.RunRecordedAsync(i % 2 == 0 ? MarketSyncKind.Manual : MarketSyncKind.Scheduled, null, null, null, CancellationToken.None)));

        most.ShouldBe(1);
        runs.ShouldAllBe(r => r.Outcome == MarketSyncOutcome.Succeeded);
        (await RateCount(factory)).ShouldBe(_source.Rates.Count);
    }

    // ---- 6. Estado ----

    [Fact]
    public async Task The_status_tells_the_history_the_schedule_and_the_runs()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        await client.PostAsync("/admin/market-data/cdi/sync", null);
        _clock.UtcNow = Monday.AddMinutes(5);
        _source.Failure = new MarketDataException("fora do ar");
        await client.PostAsync("/admin/market-data/cdi/sync", null);

        var status = (await client.GetFromJsonAsync<List<StatusDto>>("/admin/market-data"))!.Single();

        status.Code.ShouldBe("cdi");
        status.Name.ShouldBe("CDI diário");
        status.Source.ShouldBe("bcb-sgs-12");
        status.Count.ShouldBe(_source.Rates.Count);
        (status.First, status.Last, status.ExpectedLast).ShouldBe((_source.Rates.Min(r => r.Date), LastPublished, LastPublished));
        (status.Stale, status.Complete, status.MissingBusinessDayCount).ShouldBe((false, true, 0));
        status.LastFetchedAt.ShouldBe(Monday);
        status.LastRevisedAt.ShouldBeNull();
        status.Schedule.RunTimes.ShouldBe(["19:30", "23:30"]);
        status.Schedule.NextRunUtc.ShouldBe(new DateTime(2026, 10, 19, 22, 30, 0, DateTimeKind.Utc)); // 19h30 em São Paulo
        status.Schedule.Enabled.ShouldBeFalse(); // a tarefa fica desligada nos testes
        status.LastRun!.Outcome.ShouldBe("SourceUnavailable");
        status.LastSuccessfulRun!.Outcome.ShouldBe("Succeeded");
    }

    [Fact]
    public async Task With_nothing_stored_the_status_says_so()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);

        var status = (await client.GetFromJsonAsync<List<StatusDto>>("/admin/market-data"))!.Single();

        status.Count.ShouldBe(0);
        (status.First, status.Last, status.Stale, status.LastRun, status.LastSuccessfulRun).ShouldBe((null, null, true, null, null));
    }

    [Fact]
    public async Task The_runs_come_newest_first_with_a_default_and_a_ceiling()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 120; i++)
            {
                var at = Monday.AddMinutes(-i);
                db.MarketSyncRuns.Add(MarketSyncRun.Succeeded(
                    ReferenceRateSeries.Cdi, MarketSyncKind.Scheduled, at, at, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 19), i, 0, 0, 0));
            }

            await db.SaveChangesAsync();
        }

        var defaults = (await client.GetFromJsonAsync<List<RunDto>>("/admin/market-data/cdi/runs"))!;
        var capped = (await client.GetFromJsonAsync<List<RunDto>>("/admin/market-data/cdi/runs?limit=1000"))!;
        var three = (await client.GetFromJsonAsync<List<RunDto>>("/admin/market-data/cdi/runs?limit=3"))!;
        var zero = (await client.GetFromJsonAsync<List<RunDto>>("/admin/market-data/cdi/runs?limit=0"))!;

        defaults.Count.ShouldBe(20);
        capped.Count.ShouldBe(100);
        three.Select(r => r.Fetched).ShouldBe([0, 1, 2]); // a mais nova primeiro
        zero.Count.ShouldBe(1);
    }

    // ---- 7. Limpeza de 90 dias ----

    [Fact]
    public async Task Each_run_deletes_the_runs_older_than_ninety_days()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var days in new[] { 120, 91, 89, 30 })
            {
                var at = Monday.AddDays(-days);
                db.MarketSyncRuns.Add(MarketSyncRun.Succeeded(
                    ReferenceRateSeries.Cdi, MarketSyncKind.Scheduled, at, at, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), days, 0, 0, 0));
            }

            await db.SaveChangesAsync();
        }

        await client.PostAsync("/admin/market-data/cdi/sync", null);

        (await Runs(factory)).Select(r => r.Fetched).Where(f => f != _source.Rates.Count).Order().ShouldBe([30, 89]);
    }

    // ---- 8. Teto de espera ----

    [Fact]
    public async Task A_sync_that_takes_too_long_gives_504_and_is_recorded_as_timed_out()
    {
        await using var factory = await AdminFactory(("Admin:SyncTimeoutSeconds", "1"));
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        _source.Delay = TimeSpan.FromSeconds(30);

        var started = DateTime.UtcNow;
        var response = await client.PostAsync("/admin/market-data/cdi/sync", null);

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(15));
        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        using var problem = await Json(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("SyncTimedOut");
        (await RateCount(factory)).ShouldBe(0);
        (await Runs(factory)).Single().Outcome.ShouldBe(MarketSyncOutcome.TimedOut);
    }

    // ---- 9. Log ----

    [Fact]
    public async Task The_request_is_logged_with_the_user_and_the_window_and_no_value_or_email()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);
        var userId = (await client.GetFromJsonAsync<MeDto>("/auth/me"))!.Id;

        await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2026-10-01", to = "2026-10-16" });

        var requested = _logs.Entries.Single(e => e.EventId.Name == "MarketDataSyncRequested");
        requested["UserId"].ShouldBe(userId);
        requested["Series"].ShouldBe("cdi");
        requested["From"].ShouldBe(new DateOnly(2026, 10, 1));
        requested["To"].ShouldBe(new DateOnly(2026, 10, 16));
        var finished = _logs.Entries.Single(e => e.EventId.Name == "MarketDataSyncFinished");
        finished["Outcome"].ShouldBe("Succeeded");
        _logs.Entries.Where(e => e.EventId.Name?.StartsWith("MarketData") == true).ShouldAllBe(e => !e.Message.Contains(_adminEmail));
    }

    [Fact]
    public async Task The_error_code_of_the_provider_is_logged_as_digits_only()
    {
        await using var factory = await AdminFactory();
        var client = await Admin(factory);
        _source.Failure = new MarketDataException("html", sourceErrorCode: "7240913300019");

        await client.PostAsync("/admin/market-data/cdi/sync", null);

        _logs.Entries.Single(e => e.EventId.Name == "MarketDataSourceErrorCode")["SourceErrorCode"].ShouldBe("7240913300019");
    }

    // ---- 10. Limite de taxa ----

    [Fact]
    public async Task The_posts_are_limited_per_person_and_the_reads_are_not()
    {
        await using var factory = await AdminFactory(("RateLimiting:AdminSync:PermitLimit", "2"));
        var client = await Admin(factory);
        Publish(FirstDay, LastPublished);

        (await client.PostAsync("/admin/market-data/cdi/sync", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync("/admin/market-data/cdi/sync", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync("/admin/market-data/cdi/sync", null)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.PostAsJsonAsync("/admin/market-data/cdi/refresh", new { from = "2026-10-01", to = "2026-10-16" })).StatusCode
            .ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/admin/market-data")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _source.Calls.ShouldBe(2);
    }

    // ---- 11. Arquitetura da rota ----

    // Toda rota sob /admin carrega a política Admin (docs/admin-mercado.md, 6): uma rota nova sem ela quebra o build.
    [Fact]
    public async Task Every_admin_route_requires_the_admin_policy()
    {
        await using var factory = await AdminFactory();
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/admin", StringComparison.Ordinal))
            .ToList();

        endpoints.Count.ShouldBeGreaterThanOrEqualTo(4);
        var without = endpoints
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AdminOptions.PolicyName))
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        without.ShouldBeEmpty("Rota /admin sem a política Admin.");
        endpoints.ShouldAllBe(e => !e.Metadata.OfType<IAllowAnonymous>().Any());
    }

    [Fact]
    public async Task The_admin_policy_is_registered()
    {
        await using var factory = await AdminFactory();
        var policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        (await policies.GetPolicyAsync(AdminOptions.PolicyName)).ShouldNotBeNull();
        factory.Services.GetRequiredService<IOptions<AdminOptions>>().Value.Emails.ShouldBe([_adminEmail]);
    }
}
