using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Contagem de pessoas no admin (docs/admin-email.md, AE.2): só números, por quantos dias sem acesso o dono escolher. O Postgres
// dos testes é compartilhado e guarda as contas dos outros testes, então os números são conferidos pela diferença antes e
// depois de criar as contas do cenário.
[Collection(ApiCollection.Name)]
public sealed class AdminUsersApiTests(PostgresFixture postgres)
{
    // Segunda, 19/10/2026, 12:00 em São Paulo.
    private readonly FakeClock _clock = new(new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc));
    private readonly string _adminEmail = $"{Guid.NewGuid():N}@admin.com.br";
    private readonly LogSink _logs = new();

    private sealed record MeDto(Guid Id);

    private sealed record SummaryDto(int Days, int Accounts, int Confirmed, int Unconfirmed, int Active, int Inactive, int NeverUsed);

    private PrismaApiFactory Factory() => new(
        postgres.ConnectionString,
        new Dictionary<string, string> { ["Admin:Emails:0"] = _adminEmail, ["RateLimiting:Auth:PermitLimit"] = "1000" },
        _clock, _logs);

    private static async Task<HttpClient> SignIn(PrismaApiFactory factory, string email)
    {
        var client = await Register(factory, email);
        await factory.ConfirmEmailAsync(email);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<HttpClient> Register(PrismaApiFactory factory, string email)
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .EnsureSuccessStatusCode();
        return client;
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    private static async Task<SummaryDto> Summary(HttpClient admin, int? days = null)
    {
        var response = await admin.GetAsync(days is null ? "/admin/users/summary" : $"/admin/users/summary?days={days}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SummaryDto>())!;
    }

    private static async Task SetLastSeen(PrismaApiFactory factory, string email, DateTime? lastSeenAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, lastSeenAt))).ShouldBe(1);
    }

    // Uma conta com o e-mail confirmado e o último acesso no meio-dia de São Paulo de "daysAgo" dias atrás (null = nunca usou).
    private async Task<string> Confirmed(PrismaApiFactory factory, int? daysAgo)
    {
        var email = NewEmail();
        await Register(factory, email);
        await factory.ConfirmEmailAsync(email);
        await SetLastSeen(factory, email, daysAgo is null ? null : _clock.UtcNow.AddDays(-daysAgo.Value));
        return email;
    }

    [Fact]
    public async Task Anonymous_gets_401_and_a_regular_person_gets_403()
    {
        await using var factory = Factory();

        (await factory.CreateHttpsClient().GetAsync("/admin/users/summary")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var regular = await SignIn(factory, NewEmail());
        (await regular.GetAsync("/admin/users/summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Counts_each_group_for_the_chosen_number_of_days()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var before = await Summary(admin, 30);

        await Confirmed(factory, daysAgo: 0);   // ativa
        await Confirmed(factory, daysAgo: 29);  // ativa: 29 < 30
        await Confirmed(factory, daysAgo: 30);  // inativa: 30 >= 30
        await Confirmed(factory, daysAgo: 31);  // inativa
        await Confirmed(factory, daysAgo: null); // confirmou e nunca usou
        await Register(factory, NewEmail());     // não confirmou
        await Register(factory, NewEmail());     // não confirmou

        var after = await Summary(admin, 30);

        after.Days.ShouldBe(30);
        (after.Accounts - before.Accounts).ShouldBe(7);
        (after.Confirmed - before.Confirmed).ShouldBe(5);
        (after.Unconfirmed - before.Unconfirmed).ShouldBe(2);
        (after.Active - before.Active).ShouldBe(2);
        (after.Inactive - before.Inactive).ShouldBe(2);
        (after.NeverUsed - before.NeverUsed).ShouldBe(1);
    }

    [Fact]
    public async Task The_number_of_days_moves_people_between_active_and_inactive()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var before7 = await Summary(admin, 7);
        var before90 = await Summary(admin, 90);

        await Confirmed(factory, daysAgo: 10); // inativa em 7 dias, ativa em 90

        var after7 = await Summary(admin, 7);
        var after90 = await Summary(admin, 90);

        (after7.Inactive - before7.Inactive).ShouldBe(1);
        (after7.Active - before7.Active).ShouldBe(0);
        (after90.Active - before90.Active).ShouldBe(1);
        (after90.Inactive - before90.Inactive).ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public async Task Active_inactive_and_never_used_always_add_up_to_the_confirmed(int days)
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        foreach (var daysAgo in new int?[] { 0, 1, 6, 7, 29, 30, 89, 90, 364, 365, 400, null })
            await Confirmed(factory, daysAgo);
        await Register(factory, NewEmail());
        // Conta sem o e-mail confirmado mas com acesso gravado (a confirmação foi desfeita depois): fora das três contagens.
        var revoked = NewEmail();
        await Register(factory, revoked);
        await SetLastSeen(factory, revoked, _clock.UtcNow.AddDays(-1));
        var neverConfirmedNeverSeen = NewEmail();
        await Register(factory, neverConfirmedNeverSeen);

        var summary = await Summary(admin, days);

        (summary.Active + summary.Inactive + summary.NeverUsed).ShouldBe(summary.Confirmed);
        (summary.Confirmed + summary.Unconfirmed).ShouldBe(summary.Accounts);
    }

    [Fact]
    public async Task The_day_changes_at_midnight_in_Sao_Paulo_not_in_UTC()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var before = await Summary(admin, 1);

        // 23:30 de 18/10 em São Paulo (02:30 UTC de 19/10): para a pessoa ainda é ontem, então já passou 1 dia.
        var yesterdayLate = await Confirmed(factory, daysAgo: null);
        await SetLastSeen(factory, yesterdayLate, new DateTime(2026, 10, 19, 2, 30, 0, DateTimeKind.Utc));
        // 00:10 de 19/10 em São Paulo (03:10 UTC): já é hoje.
        var todayEarly = await Confirmed(factory, daysAgo: null);
        await SetLastSeen(factory, todayEarly, new DateTime(2026, 10, 19, 3, 10, 0, DateTimeKind.Utc));

        var after = await Summary(admin, 1);

        (after.Active - before.Active).ShouldBe(1);
        (after.Inactive - before.Inactive).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_number_of_days_it_uses_30()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);

        var summary = await Summary(admin);

        summary.Days.ShouldBe(30);
        (await Summary(admin, 30)).ShouldBe(summary);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("366")]
    [InlineData("abc")]
    public async Task A_number_of_days_outside_1_to_365_is_refused(string days)
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);

        var response = await admin.GetAsync($"/admin/users/summary?days={days}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        if (int.TryParse(days, out _))
            (await response.Content.ReadAsStringAsync()).ShouldContain("InvalidDays");
    }

    [Fact]
    public async Task The_answer_has_only_numbers_never_data_about_a_person()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var email = await Confirmed(factory, daysAgo: 3);

        var response = await admin.GetAsync("/admin/users/summary");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(email);
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet().ShouldBe(
            ["days", "accounts", "confirmed", "unconfirmed", "active", "inactive", "neverUsed"], ignoreOrder: true);
        json.RootElement.EnumerateObject().ShouldAllBe(p => p.Value.ValueKind == JsonValueKind.Number);
    }

    [Fact]
    public async Task Who_looked_at_the_count_is_logged_with_the_days_and_never_the_numbers()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var adminId = (await admin.GetFromJsonAsync<MeDto>("/auth/me"))!.Id;
        await Confirmed(factory, daysAgo: 3);

        var summary = await Summary(admin, 45);

        var log = await _logs.WaitFor(l => l.EventId.Name == "UserSummaryRequested");
        log.Level.ShouldBe(LogLevel.Information);
        log["UserId"].ShouldBe(adminId);
        log["Days"].ShouldBe(45);
        // Os números devolvidos não vão para o log: só quem pediu e a janela.
        log.Properties.Keys.ShouldNotContain("Accounts");
        log.Properties.Keys.ShouldNotContain("Inactive");
        log.Message.ShouldNotContain(summary.Accounts.ToString() + " accounts");
    }

    [Fact]
    public async Task A_refused_number_of_days_and_a_regular_person_leave_no_summary_log()
    {
        await using var factory = Factory();
        var admin = await SignIn(factory, _adminEmail);
        var regular = await SignIn(factory, NewEmail());

        (await admin.GetAsync("/admin/users/summary?days=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await regular.GetAsync("/admin/users/summary?days=30")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // A linha da requisição é escrita quando o pipeline termina: espera a do 403 antes de conferir.
        await _logs.WaitFor(l => l.EventId.Name == "RequestFinished" && Equals(l["StatusCode"], 403));

        _logs.Entries.ShouldNotContain(l => l.EventId.Name == "UserSummaryRequested");
    }
}
