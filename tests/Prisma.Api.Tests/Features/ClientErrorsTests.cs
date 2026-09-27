using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Erros do navegador chegam ao servidor (etapa H.3b): o relato vira um log ClientError, com o userId
// da sessão quando há uma. Só mensagem, pilha, tela (sem consulta), tipo e versão.
[Collection(ApiCollection.Name)]
public sealed class ClientErrorsTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string Email);

    private static object Report(string screen = "/lancar", string kind = "crash", string? message = null) => new
    {
        message = message ?? "Cannot read properties of undefined (reading 'x')",
        stack = "TypeError: Cannot read properties of undefined\n    at index-D97u.js:1:23456",
        screen,
        kind,
        appVersion = "2026-09-26T23:59:00Z",
    };

    [Fact]
    public async Task A_report_is_logged_as_a_warning_with_the_user_of_the_session()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var me = (await client.GetFromJsonAsync<Me>("/auth/me"))!;

        var response = await client.PostAsJsonAsync("/client-errors", Report());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var line = await logs.WaitFor(l => l.EventId.Name == "ClientError");
        line.Level.ShouldBe(LogLevel.Warning);
        line["Screen"].ShouldBe("/lancar");
        line["Kind"].ShouldBe("crash");
        line["ErrorMessage"].ShouldBe("Cannot read properties of undefined (reading 'x')");
        line.Exception!.ToString().ShouldContain("index-D97u.js");
        line["AppVersion"].ShouldBe("2026-09-26T23:59:00Z");
        line["UserId"].ShouldBe(me.Id);
    }

    // A tela de entrar e a sessão expirada também quebram: o relato entra, só que sem usuário.
    [Fact]
    public async Task A_report_without_a_session_is_logged_too()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var anonymous = factory.CreateHttpsClient();

        var response = await anonymous.PostAsJsonAsync("/client-errors", Report(screen: "/entrar", kind: "silent"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var line = await logs.WaitFor(l => l.EventId.Name == "ClientError");
        line["Screen"].ShouldBe("/entrar");
        line.Properties.ShouldNotContainKey("UserId");
    }

    // A consulta do endereço pode levar dados (?estorno=<id>, e um dia uma busca): fica de fora.
    [Fact]
    public async Task The_screen_is_logged_without_the_query_string()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();

        await client.PostAsJsonAsync("/client-errors", Report(screen: "/lancar?estorno=01a0ddbc#topo"));

        (await logs.WaitFor(l => l.EventId.Name == "ClientError"))["Screen"].ShouldBe("/lancar");
    }

    [Theory]
    [InlineData("/lancar", "boom", 2001)] // mensagem grande demais
    [InlineData("/lancar", "other", 10)] // tipo desconhecido
    [InlineData("https://outro.site/x", "crash", 10)] // tela que não é um caminho do app
    public async Task An_invalid_report_is_refused_and_not_logged(string screen, string kind, int messageLength)
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, logs: logs);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var validKind = kind == "boom" ? "crash" : kind;

        var response = await client.PostAsJsonAsync("/client-errors", Report(screen, validKind, new string('x', messageLength)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await logs.WaitFor(l => l.EventId.Name == "RequestFinished" && Equals(l["StatusCode"], 400));
        logs.Entries.ShouldNotContain(l => l.EventId.Name == "ClientError");
    }

    [Fact]
    public async Task Reports_are_rate_limited()
    {
        var logs = new LogSink();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string>
            {
                ["RateLimiting:Auth:PermitLimit"] = "1000",
                ["RateLimiting:ClientErrors:PermitLimit"] = "2",
            },
            logs: logs);
        using var client = factory.CreateHttpsClient();

        await client.PostAsJsonAsync("/client-errors", Report());
        await client.PostAsJsonAsync("/client-errors", Report());
        var third = await client.PostAsJsonAsync("/client-errors", Report());

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await logs.WaitFor(l => l.EventId.Name == "RateLimited"))["Route"].ShouldBe("/client-errors");
    }
}
