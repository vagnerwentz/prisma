using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Logs para investigar a produção (etapa H.3a): uma linha por requisição, eventos declarados e o
// traceId que o cliente recebe no erro igual ao dos logs daquela requisição. Nada de e-mail ou senha.
[Collection(ApiCollection.Name)]
public sealed class LoggingTests(PostgresFixture postgres)
{
    private const string WrongPassword = "senha-errada-999";

    private sealed record Me(Guid Id, string Email);

    private sealed record Created(Guid Id);

    private sealed class BrokenClock : IClock
    {
        public bool Broken { get; set; }

        public DateTime UtcNow => Broken ? throw new InvalidOperationException("relógio quebrado") : DateTime.UtcNow;
    }

    private static string Route(CapturedLog log) => ((string)log["Route"]!).TrimEnd('/');

    private static bool IsRequest(CapturedLog log, string route, int status) =>
        log.EventId.Name == "RequestFinished" && Route(log) == route && Equals(log["StatusCode"], status);

    private static async Task<string> TraceIdOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        var traceId = ((JsonElement)problem!.Extensions["traceId"]!).GetString()!;
        traceId.ShouldMatch("^[0-9a-f]{32}$");
        return traceId;
    }

    [Fact]
    public async Task Each_api_request_logs_one_line_with_route_status_duration_and_user()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var me = (await client.GetFromJsonAsync<Me>("/auth/me"))!;

        (await client.GetAsync("/accounts")).EnsureSuccessStatusCode();

        var line = await logs.WaitFor(l => IsRequest(l, "/accounts", 200));
        line.Level.ShouldBe(LogLevel.Information);
        line["Method"].ShouldBe("GET");
        line["UserId"].ShouldBe(me.Id);
        ((double)line["ElapsedMs"]!).ShouldBeGreaterThanOrEqualTo(0);
        line.TraceId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Creating_an_account_logs_its_id_and_type_but_not_its_name_or_balance()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/accounts",
            new { name = "Conta Secreta", type = "Checking", initialBalanceCents = 123456 });
        var created = (await response.Content.ReadFromJsonAsync<Created>())!;

        var line = await logs.WaitFor(l => l.EventId.Name == "AccountCreated");
        line["AccountId"].ShouldBe(created.Id);
        line["AccountType"].ShouldBe(AccountType.Checking);
        logs.Entries.ShouldAllBe(l => !l.Message.Contains("Conta Secreta") && !l.Message.Contains("123456"));
    }

    [Fact]
    public async Task An_error_response_carries_the_trace_id_of_its_request_log()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/accounts", new { name = "", type = "Checking", initialBalanceCents = 0 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var traceId = await TraceIdOf(response);
        var line = await logs.WaitFor(l => IsRequest(l, "/accounts", 400));
        line.TraceId.ShouldBe(traceId);
    }

    [Fact]
    public async Task An_unhandled_exception_is_an_error_log_with_the_trace_id_sent_to_the_client()
    {
        var logs = new LogSink();
        var clock = new BrokenClock();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();
        clock.Broken = true;

        var response = await client.GetAsync("/dashboard/summary");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var traceId = await TraceIdOf(response);
        var error = await logs.WaitFor(l => l.EventId.Name == "UnhandledException");
        error.Level.ShouldBe(LogLevel.Error);
        error.Exception.ShouldBeOfType<InvalidOperationException>();
        error.TraceId.ShouldBe(traceId);
        Route(error).ShouldBe("/dashboard/summary");
    }

    [Fact]
    public async Task A_failed_sign_in_is_a_warning_with_the_reason_and_never_the_email_or_password()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        var email = $"{Guid.NewGuid():N}@teste.com.br";
        using var registered = factory.CreateHttpsClient();
        var me = (await (await registered.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password }))
            .Content.ReadFromJsonAsync<Me>())!;
        using var anonymous = factory.CreateHttpsClient();

        await anonymous.PostAsJsonAsync("/auth/login", new { email, password = WrongPassword });
        await anonymous.PostAsJsonAsync("/auth/login", new { email = "ninguem@teste.com.br", password = WrongPassword });

        var wrongPassword = await logs.WaitFor(l => l.EventId.Name == "LoginFailed" && Equals(l["Reason"], "WrongPassword"));
        wrongPassword.Level.ShouldBe(LogLevel.Warning);
        wrongPassword["UserId"].ShouldBe(me.Id);
        var unknown = await logs.WaitFor(l => l.EventId.Name == "LoginUnknownEmail");
        unknown.Level.ShouldBe(LogLevel.Warning);
        unknown.Properties.ShouldNotContainKey("UserId");

        await logs.WaitFor(l => l.EventId.Name == "UserRegistered");
        foreach (var log in logs.Entries)
        {
            var text = log.Message + " " + string.Join(" ", log.Properties.Values);
            text.ShouldNotContain(email);
            text.ShouldNotContain("ninguem@teste.com.br");
            text.ShouldNotContain(WrongPassword);
            text.ShouldNotContain(PrismaApiFactory.Password);
        }
    }

    [Fact]
    public async Task Hitting_the_rate_limit_is_a_warning_with_the_route()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string> { ["RateLimiting:Auth:PermitLimit"] = "1" }, logs: logs);
        using var client = factory.CreateHttpsClient();

        await client.PostAsJsonAsync("/auth/login", new { email = "a@teste.com.br", password = WrongPassword });
        var limited = await client.PostAsJsonAsync("/auth/login", new { email = "a@teste.com.br", password = WrongPassword });

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var line = await logs.WaitFor(l => l.EventId.Name == "RateLimited");
        line.Level.ShouldBe(LogLevel.Warning);
        Route(line).ShouldBe("/auth/login");
    }
}
