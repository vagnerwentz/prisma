using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Tests.Email;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Disparos de e-mail pelo admin (docs/admin-email.md, seção 4; AE.4): um modelo e um público, com prévia, teste, confirmação da
// contagem, ninguém duas vezes e o teto por disparo. Cada teste roda num banco próprio: o compartilhado guarda contas não
// confirmadas de outros testes, e o público (e o teto) viraria imprevisível.
[Collection(ApiCollection.Name)]
public sealed class AdminEmailCampaignsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Segunda, 19/10/2026, 12:00 em São Paulo.
    private static readonly DateTime Start = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);
    private readonly LogSink _logs = new();
    private readonly string _adminEmail = $"{Guid.NewGuid():N}@admin.com.br";
    private readonly string _database = $"campaigns_{Guid.NewGuid():N}";
    private string _connectionString = "";

    private sealed record TemplateDto(
        string Code, string Name, string Description, string Category, string Audience, int DefaultDays, int? MinDaysBetween,
        bool Sendable, string? BlockedCode);

    private sealed record AudienceDto(string Template, int Days, int Count, int Cap);

    private sealed record PreviewDto(string Subject, string Html, string Text);

    private sealed record CampaignDto(Guid Id, string Template, int Days, int Recipients, DateTime CreatedAt);

    private sealed record MeDto(Guid Id);

    public async Task InitializeAsync()
    {
        await Execute(postgres.ConnectionString, $"CREATE DATABASE {_database}");
        _connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await Execute(postgres.ConnectionString, $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)");
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private PrismaApiFactory Factory(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string>
        {
            ["Database:MigrateOnStartup"] = "true",
            ["Admin:Emails:0"] = _adminEmail,
            ["RateLimiting:Auth:PermitLimit"] = "1000",
            ["RateLimiting:AdminEmail:PermitLimit"] = "1000",
            ["Email:Outbox:BatchSize"] = "500",
        };
        foreach (var (key, value) in extra)
            settings[key] = value;
        return new PrismaApiFactory(_connectionString, settings, _clock, _logs);
    }

    private static async Task<HttpClient> SignIn(PrismaApiFactory factory, string email)
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    // Uma conta criada direto, com a data de cadastro, a confirmação e o último acesso que o cenário pede.
    private async Task<(Guid Id, string Email)> Seed(PrismaApiFactory factory, bool confirmed, int createdDaysAgo, int? seenDaysAgo = null)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = NewEmail();
        var user = new AppUser { UserName = email, Email = email };
        (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdAt = Start.AddDays(-createdDaysAgo);
        DateTime? seen = seenDaysAgo is null ? null : Start.AddDays(-seenDaysAgo.Value);
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.CreatedAt, createdAt)
            .SetProperty(u => u.EmailConfirmed, confirmed)
            .SetProperty(u => u.LastSeenAt, seen));
        return (user.Id, email);
    }

    private static async Task<AudienceDto> Audience(HttpClient admin, string template, int days)
    {
        var response = await admin.GetAsync($"/admin/emails/audience?template={template}&days={days}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AudienceDto>())!;
    }

    private static Task<HttpResponseMessage> Send(HttpClient admin, string template, int days, int expected, Guid? requestId = null) =>
        admin.PostAsJsonAsync("/admin/emails/send", new { template, days, expectedCount = expected, clientRequestId = requestId ?? Guid.NewGuid() });

    private static async Task<List<OutboxEmail>> Outbox(PrismaApiFactory factory, EmailKind kind)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxEmails.AsNoTracking()
            .Where(e => e.Kind == kind).OrderBy(e => e.NextAttemptAt).ToListAsync();
    }

    private static Task<EmailRunSummary> RunQueue(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<EmailOutboxProcessor>().RunOnceAsync(CancellationToken.None);

    private static FakeEmailTransport Transport(PrismaApiFactory factory) => factory.Services.GetRequiredService<FakeEmailTransport>();

    private static async Task<string> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    // ---- quem pode ----

    [Fact]
    public async Task Every_route_refuses_anonymous_with_401_and_a_regular_person_with_403()
    {
        await using var factory = Factory();
        var anonymous = factory.CreateHttpsClient();
        var regular = await SignIn(factory, NewEmail());

        var requests = new Func<HttpClient, Task<HttpResponseMessage>>[]
        {
            c => c.GetAsync("/admin/emails/templates"),
            c => c.GetAsync("/admin/emails/audience?template=confirm-reminder&days=3"),
            c => c.PostAsJsonAsync("/admin/emails/preview", new { template = "confirm-reminder" }),
            c => c.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" }),
            c => c.PostAsJsonAsync("/admin/emails/send", new { template = "confirm-reminder", days = 3, expectedCount = 1, clientRequestId = Guid.NewGuid() }),
            c => c.GetAsync("/admin/emails/campaigns"),
        };
        foreach (var request in requests)
        {
            (await request(anonymous)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await request(regular)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();
    }

    // ---- modelos ----

    [Fact]
    public async Task The_templates_say_which_can_go_and_why_the_others_cannot()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);

        var templates = (await admin.GetFromJsonAsync<List<TemplateDto>>("/admin/emails/templates"))!;

        var reminder = templates.Single(t => t.Code == "confirm-reminder");
        reminder.Category.ShouldBe("Account");
        reminder.Sendable.ShouldBeTrue();
        reminder.BlockedCode.ShouldBeNull();
        reminder.DefaultDays.ShouldBe(3);
        reminder.MinDaysBetween.ShouldBeNull(); // uma vez por conta

        var missYou = templates.Single(t => t.Code == "miss-you");
        missYou.Category.ShouldBe("Relationship");
        missYou.Sendable.ShouldBeFalse();
        missYou.BlockedCode.ShouldBe("UnsubscribeRequired");
        missYou.DefaultDays.ShouldBe(30);
    }

    // ---- público ----

    [Fact]
    public async Task The_reminder_audience_is_the_unconfirmed_accounts_at_least_N_days_old()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 3);  // entra: 3 dias
        await Seed(factory, confirmed: false, createdDaysAgo: 40); // entra
        await Seed(factory, confirmed: false, createdDaysAgo: 2);  // ainda não
        await Seed(factory, confirmed: false, createdDaysAgo: 0);  // ainda não
        await Seed(factory, confirmed: true, createdDaysAgo: 40);  // confirmou

        var audience = await Audience(admin, "confirm-reminder", 3);

        audience.Count.ShouldBe(2);
        audience.Cap.ShouldBe(50);
        (await Audience(admin, "confirm-reminder", 1)).Count.ShouldBe(3); // o de 2 dias entra com N=1; o de hoje não
    }

    [Fact]
    public async Task The_miss_you_audience_is_the_confirmed_accounts_without_access_for_N_days_and_never_the_unused_ones()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 30); // entra
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 100); // entra
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 29); // ainda não
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: null); // sem acesso gravado: fora
        await Seed(factory, confirmed: false, createdDaysAgo: 90, seenDaysAgo: 100); // não confirmou: fora

        (await Audience(admin, "miss-you", 30)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_audience_refuses_an_unknown_template_and_days_outside_1_to_365()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);

        (await admin.GetAsync("/admin/emails/audience?template=nope&days=3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var days in new[] { 0, -1, 366 })
        {
            var response = await admin.GetAsync($"/admin/emails/audience?template=confirm-reminder&days={days}");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorCode(response)).ShouldBe("InvalidDays");
        }
    }

    // ---- prévia e teste ----

    [Theory]
    [InlineData("confirm-reminder", "Falta só confirmar")]
    [InlineData("miss-you", "Sentimos sua falta")]
    public async Task The_preview_renders_the_email_with_example_data_never_a_real_person(string template, string subjectStart)
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var (_, real) = await Seed(factory, confirmed: false, createdDaysAgo: 10, seenDaysAgo: null);

        var response = await admin.PostAsJsonAsync("/admin/emails/preview", new { template });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<PreviewDto>())!;
        preview.Subject.ShouldStartWith(subjectStart);
        preview.Html.ShouldContain("<html");
        preview.Html.ShouldContain("Prisma Money");
        preview.Html.ShouldContain("pessoa@exemplo.com.br");
        preview.Text.ShouldNotBeNullOrWhiteSpace();
        preview.Html.ShouldNotContain(real);
        preview.Html.ShouldNotContain(_adminEmail);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_refuses_an_unknown_template()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);

        (await admin.PostAsJsonAsync("/admin/emails/preview", new { template = "nope" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("confirm-reminder")]
    [InlineData("miss-you")] // o teste vai só ao dono: pode, mesmo com o modelo bloqueado para o público
    public async Task The_test_goes_only_to_the_owner_with_a_marked_subject(string template)
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var (_, other) = await Seed(factory, confirmed: false, createdDaysAgo: 10);
        var before = Transport(factory).Messages.Count;

        var response = await admin.PostAsJsonAsync("/admin/emails/test", new { template });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var sent = Transport(factory).Messages.Skip(before).ToList();
        sent.Count.ShouldBe(1);
        sent[0].To.ShouldBe(_adminEmail, StringCompareShould.IgnoreCase);
        sent[0].Subject.ShouldStartWith("[Teste] ");
        sent[0].Html.ShouldNotContain(other);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty(); // não passa pela fila nem conta como disparo
    }

    [Fact]
    public async Task A_test_the_provider_refused_says_so()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        Transport(factory).Respond = _ => Task.FromResult(EmailSendResult.Reject("http_422"));

        var response = await admin.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await ErrorCode(response)).ShouldBe("SendFailed");
    }

    // ---- disparo ----

    [Fact]
    public async Task The_send_queues_one_email_per_person_spread_in_time_and_the_worker_sends_them_with_a_fresh_link()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var stuck = new List<(Guid Id, string Email)>
        {
            await Seed(factory, confirmed: false, createdDaysAgo: 5),
            await Seed(factory, confirmed: false, createdDaysAgo: 8),
            await Seed(factory, confirmed: false, createdDaysAgo: 30),
        };
        var (_, recent) = await Seed(factory, confirmed: false, createdDaysAgo: 1);
        var (_, confirmed) = await Seed(factory, confirmed: true, createdDaysAgo: 30);
        var audience = await Audience(admin, "confirm-reminder", 3);
        audience.Count.ShouldBe(3);

        var response = await Send(admin, "confirm-reminder", 3, audience.Count);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var campaign = (await response.Content.ReadFromJsonAsync<CampaignDto>())!;
        campaign.Template.ShouldBe("confirm-reminder");
        campaign.Days.ShouldBe(3);
        campaign.Recipients.ShouldBe(3);

        // Na fila, escalonados: o e-mail de senha de outra pessoa passa à frente de um disparo.
        var queued = await Outbox(factory, EmailKind.ConfirmEmailReminder);
        queued.Select(e => e.UserId).ShouldBe(stuck.Select(s => s.Id), ignoreOrder: true);
        queued.Select(e => e.NextAttemptAt).Distinct().Count().ShouldBe(3);
        queued[0].NextAttemptAt.ShouldBe(Start);
        queued.ShouldAllBe(e => e.Status == EmailStatus.Pending);

        // Antes da hora dos outros, só o primeiro sai.
        var first = await RunQueue(factory);
        Transport(factory).Messages.Count(m => stuck.Any(s => s.Email == m.To)).ShouldBe(1);
        first.Sent.ShouldBeGreaterThanOrEqualTo(1);

        _clock.UtcNow = Start.AddHours(1);
        await RunQueue(factory);

        foreach (var (id, email) in stuck)
        {
            var mail = Transport(factory).To(email).Single();
            mail.Subject.ShouldStartWith("Falta só confirmar");
            mail.Html.ShouldContain("confirmar-email#u=" + id);
            mail.Html.ShouldContain(email);
        }

        Transport(factory).To(recent).ShouldBeEmpty();
        Transport(factory).To(confirmed).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nobody_gets_the_same_reminder_twice_and_the_second_send_finds_no_one()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 5);
        await Seed(factory, confirmed: false, createdDaysAgo: 9);
        (await Send(admin, "confirm-reminder", 3, 2)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Audience(admin, "confirm-reminder", 3)).Count.ShouldBe(0);
        var again = await Send(admin, "confirm-reminder", 3, 0);

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(again)).ShouldBe("NoRecipients");
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).Count.ShouldBe(2);

        // Nem uma conta nova no público faz as antigas voltarem: só ela entra.
        await Seed(factory, confirmed: false, createdDaysAgo: 4);
        (await Audience(admin, "confirm-reminder", 3)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Repeating_a_request_returns_the_same_campaign_and_queues_nothing_more()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 5);
        var requestId = Guid.NewGuid();

        var first = (await (await Send(admin, "confirm-reminder", 3, 1, requestId)).Content.ReadFromJsonAsync<CampaignDto>())!;
        var second = await Send(admin, "confirm-reminder", 3, 1, requestId); // o toque duplicado: a audiência agora é 0

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<CampaignDto>())!.Id.ShouldBe(first.Id);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).Count.ShouldBe(1);
        (await admin.GetFromJsonAsync<List<CampaignDto>>("/admin/emails/campaigns"))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_count_the_owner_did_not_see_is_a_conflict_and_nothing_is_queued()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 5);
        await Seed(factory, confirmed: false, createdDaysAgo: 6);

        var response = await Send(admin, "confirm-reminder", 3, expected: 1);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(response)).ShouldBe("AudienceChanged");
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_audience_over_the_cap_is_refused_and_nothing_is_queued()
    {
        await using var factory = Factory(("Email:Campaigns:MaxRecipients", "3"));
        var admin = await SignIn(factory, _adminEmail);
        for (var i = 0; i < 4; i++)
            await Seed(factory, confirmed: false, createdDaysAgo: 5 + i);

        var audience = await Audience(admin, "confirm-reminder", 3);
        audience.Cap.ShouldBe(3);
        audience.Count.ShouldBe(4);
        var response = await Send(admin, "confirm-reminder", 3, 4);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe("TooManyRecipients");
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_relationship_email_cannot_be_sent_while_there_is_no_unsubscribe_even_with_an_audience()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 60);
        var audience = await Audience(admin, "miss-you", 30);
        audience.Count.ShouldBe(1);

        var response = await Send(admin, "miss-you", 30, audience.Count);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe("UnsubscribeRequired");
        (await Outbox(factory, EmailKind.MissYou)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bad_requests_are_refused_before_anything_is_queued()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 5);

        (await Send(admin, "nope", 3, 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(admin, "confirm-reminder", 0, 1)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(admin, "confirm-reminder", 366, 1)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/admin/emails/send",
            new { template = "confirm-reminder", days = 3, expectedCount = 1, clientRequestId = Guid.Empty }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_sends_at_the_same_time_never_queue_the_same_people_twice()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        for (var i = 0; i < 3; i++)
            await Seed(factory, confirmed: false, createdDaysAgo: 5 + i);

        var responses = await Task.WhenAll(Send(admin, "confirm-reminder", 3, 3), Send(admin, "confirm-reminder", 3, 3));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).Count.ShouldBe(3);
    }

    // Determinístico, ao contrário de duas chamadas ao mesmo tempo (que acabam em série de tão rápidas): o teste segura o lock do
    // modelo, dispara o envio e confere que ele espera; só ao soltar o lock o envio termina.
    [Fact]
    public async Task A_send_waits_while_another_send_of_the_same_template_holds_the_lock()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: false, createdDaysAgo: 5);

        await using var holder = new NpgsqlConnection(_connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", holder, transaction))
        {
            hold.Parameters.AddWithValue("key", Prisma.Api.Features.Admin.SendEmailCampaign.LockKey(EmailKind.ConfirmEmailReminder));
            await hold.ExecuteNonQueryAsync();
        }

        var send = Send(admin, "confirm-reminder", 3, 1);
        await Task.Delay(1500);
        send.IsCompleted.ShouldBeFalse();
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).ShouldBeEmpty();

        await transaction.CommitAsync();

        (await send).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).Count.ShouldBe(1);
    }

    // Outro modelo não espera por este.
    [Fact]
    public async Task The_lock_is_per_template_so_another_template_does_not_wait()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 60);

        await using var holder = new NpgsqlConnection(_connectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", holder, transaction))
        {
            hold.Parameters.AddWithValue("key", Prisma.Api.Features.Admin.SendEmailCampaign.LockKey(EmailKind.ConfirmEmailReminder));
            await hold.ExecuteNonQueryAsync();
        }

        // O "sentimos sua falta" é recusado (sem descadastro), mas só depois de passar pelo lock do próprio modelo: responde na hora.
        var response = await Send(admin, "miss-you", 30, 1).WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await transaction.RollbackAsync();
    }

    // ---- na hora do envio ----

    [Fact]
    public async Task Someone_who_confirmed_after_the_send_was_queued_gets_nothing()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var (id, email) = await Seed(factory, confirmed: false, createdDaysAgo: 5);
        (await Send(admin, "confirm-reminder", 3, 1)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.ConfirmEmailAsync(email);
        _clock.UtcNow = Start.AddHours(1);
        var summary = await RunQueue(factory);

        Transport(factory).To(email).ShouldBeEmpty();
        summary.Skipped.ShouldBeGreaterThanOrEqualTo(1);
        (await Outbox(factory, EmailKind.ConfirmEmailReminder)).Single().Status.ShouldBe(EmailStatus.Skipped);
        id.ShouldNotBe(Guid.Empty);
    }

    // O "sentimos sua falta" ainda não sai pelo disparo, mas o montador existe e a prévia o mostra: aqui, ele sozinho.
    [Fact]
    public async Task The_miss_you_email_is_composed_with_the_sign_in_link_and_skipped_for_someone_who_came_back()
    {
        await using var factory = Factory();
        var gone = await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 60);
        var back = await Seed(factory, confirmed: true, createdDaysAgo: 90, seenDaysAgo: 60);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxEmails.Add(OutboxEmail.Create(EmailKind.MissYou, gone.Id, gone.Email, Start));
            db.OutboxEmails.Add(OutboxEmail.Create(EmailKind.MissYou, back.Id, back.Email, Start));
            await db.SaveChangesAsync();
            // Voltou depois de o e-mail entrar na fila.
            await db.Users.Where(u => u.Id == back.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, Start.AddMinutes(5)));
        }

        _clock.UtcNow = Start.AddMinutes(10);
        await RunQueue(factory);

        var mail = Transport(factory).To(gone.Email).Single();
        mail.Subject.ShouldStartWith("Sentimos sua falta");
        mail.Html.ShouldContain("/entrar");
        Transport(factory).To(back.Email).ShouldBeEmpty();
    }

    // ---- histórico, log e limite ----

    [Fact]
    public async Task The_history_lists_the_campaigns_without_any_data_about_a_person()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var (_, email) = await Seed(factory, confirmed: false, createdDaysAgo: 5);
        (await Send(admin, "confirm-reminder", 3, 1)).EnsureSuccessStatusCode();

        var response = await admin.GetAsync("/admin/emails/campaigns");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain(email);
        var campaigns = JsonSerializer.Deserialize<List<CampaignDto>>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        campaigns.Count.ShouldBe(1);
        campaigns[0].Recipients.ShouldBe(1);
        campaigns[0].CreatedAt.ShouldBe(Start);
        using var json = JsonDocument.Parse(body);
        json.RootElement[0].EnumerateObject().Select(p => p.Name).ToHashSet().ShouldBe(
            ["id", "template", "days", "recipients", "createdAt"], ignoreOrder: true);
    }

    [Fact]
    public async Task Who_sent_what_is_logged_with_ids_and_counts_never_an_address()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var adminId = (await admin.GetFromJsonAsync<MeDto>("/auth/me"))!.Id;
        var (_, email) = await Seed(factory, confirmed: false, createdDaysAgo: 5);

        (await Send(admin, "confirm-reminder", 3, 1)).EnsureSuccessStatusCode();
        (await Send(admin, "miss-you", 30, 0)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" })).EnsureSuccessStatusCode();

        var sent = await _logs.WaitFor(l => l.EventId.Name == "EmailCampaignSent");
        sent.Level.ShouldBe(LogLevel.Information);
        sent["UserId"].ShouldBe(adminId);
        sent["Kind"].ShouldBe(EmailKind.ConfirmEmailReminder);
        sent["Recipients"].ShouldBe(1);
        sent["Days"].ShouldBe(3);

        var refused = await _logs.WaitFor(l => l.EventId.Name == "EmailCampaignRefused");
        refused.Level.ShouldBe(LogLevel.Warning);
        refused["UserId"].ShouldBe(adminId);
        refused["Code"].ShouldBe("UnsubscribeRequired");

        var test = await _logs.WaitFor(l => l.EventId.Name == "EmailTestSent");
        test["UserId"].ShouldBe(adminId);

        _logs.Entries
            .Where(l => l.EventId.Name is "EmailCampaignSent" or "EmailCampaignRefused" or "EmailTestSent")
            .ShouldAllBe(l => !l.Message.Contains(email) && !l.Message.Contains(_adminEmail));
    }

    [Fact]
    public async Task Sending_and_testing_share_a_limit_per_person()
    {
        await using var factory = Factory(("RateLimiting:AdminEmail:PermitLimit", "2"));
        var admin = await SignIn(factory, _adminEmail);

        (await admin.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/admin/emails/test", new { template = "confirm-reminder" })).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Send(admin, "confirm-reminder", 3, 1)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    // Os e-mails de conta e o disparo não se misturam no limite por pessoa e por tipo da fila (EmailQueue).
    [Fact]
    public async Task The_send_does_not_use_up_the_queue_limit_of_the_normal_confirmation()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var (_, email) = await Seed(factory, confirmed: false, createdDaysAgo: 5);
        (await Send(admin, "confirm-reminder", 3, 1)).EnsureSuccessStatusCode();

        (await factory.CreateHttpsClient().PostAsJsonAsync("/auth/resend-confirmation", new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await Outbox(factory, EmailKind.ConfirmEmail)).ShouldContain(e => e.ToAddress == email);
    }
}
