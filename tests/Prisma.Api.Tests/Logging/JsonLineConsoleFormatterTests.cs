using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Logging;

// Produção escreve uma linha JSON por evento, no formato que o Railway filtra: `level` e `message`
// reconhecidos, propriedades no primeiro nível (etapa H.3a).
public sealed class JsonLineConsoleFormatterTests
{
    private static readonly FakeClock Clock = new(new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc));

    private static KeyValuePair<string, object?> Pair(string key, object? value) => new(key, value);

    private static JsonElement Write(
        LogLevel level,
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string message = "Something happened",
        Exception? exception = null,
        IExternalScopeProvider? scopes = null,
        EventId eventId = default)
    {
        var formatter = new JsonLineConsoleFormatter(Clock);
        using var writer = new StringWriter();
        formatter.Write(
            new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(level, "Prisma.Test", eventId, state, exception, (_, _) => message),
            scopes,
            writer);

        var text = writer.ToString();
        text.ShouldEndWith(Environment.NewLine);
        text.TrimEnd().ShouldNotContain('\n');
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public void Writes_one_line_with_level_message_event_and_properties_at_the_top()
    {
        var userId = Guid.Parse("01a0ddbc-628e-7949-a0ed-9180cd3b704f");

        var json = Write(
            LogLevel.Warning,
            [Pair("UserId", userId), Pair("StatusCode", 401), Pair("{OriginalFormat}", "Sign-in failed: {Reason}")],
            message: "Sign-in failed: WrongPassword",
            eventId: new EventId(2003, "LoginFailed"));

        json.GetProperty("timestamp").GetString().ShouldStartWith("2026-09-26T15:30:00");
        json.GetProperty("level").GetString().ShouldBe("warn");
        json.GetProperty("message").GetString().ShouldBe("Sign-in failed: WrongPassword");
        json.GetProperty("category").GetString().ShouldBe("Prisma.Test");
        json.GetProperty("eventId").GetInt32().ShouldBe(2003);
        json.GetProperty("eventName").GetString().ShouldBe("LoginFailed");
        json.GetProperty("userId").GetString().ShouldBe(userId.ToString());
        json.GetProperty("statusCode").GetInt32().ShouldBe(401);
        json.TryGetProperty("{OriginalFormat}", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(LogLevel.Trace, "debug")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "error")]
    public void Levels_use_the_names_railway_recognizes(LogLevel level, string expected) =>
        Write(level, []).GetProperty("level").GetString().ShouldBe(expected);

    // Dos escopos, só o que identifica a requisição e a pessoa: o caminho cru pode levar dados.
    [Fact]
    public void Takes_only_trace_span_and_user_from_scopes()
    {
        var scopes = new LoggerExternalScopeProvider();
        using var request = scopes.Push(new[]
        {
            Pair("TraceId", "4bf92f3577b34da6a3ce929d0e0e4736"),
            Pair("SpanId", "00f067aa0ba902b7"),
            Pair("RequestPath", "/api/transactions?statementId=secret"),
            Pair("ConnectionId", "0HN1"),
        });
        using var user = scopes.Push(new[] { Pair("UserId", "01a0ddbc-628e-7949-a0ed-9180cd3b704f") });

        var json = Write(LogLevel.Information, [], scopes: scopes);

        json.GetProperty("traceId").GetString().ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
        json.GetProperty("spanId").GetString().ShouldBe("00f067aa0ba902b7");
        json.GetProperty("userId").GetString().ShouldBe("01a0ddbc-628e-7949-a0ed-9180cd3b704f");
        json.TryGetProperty("requestPath", out _).ShouldBeFalse();
        json.TryGetProperty("connectionId", out _).ShouldBeFalse();
    }

    [Fact]
    public void Keeps_the_exception_with_its_stack_in_the_same_line()
    {
        Exception exception;
        try
        {
            throw new InvalidOperationException("broken clock");
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        var json = Write(LogLevel.Error, [], exception: exception);

        var text = json.GetProperty("exception").GetString()!;
        text.ShouldContain("System.InvalidOperationException: broken clock");
        text.ShouldContain(nameof(Keeps_the_exception_with_its_stack_in_the_same_line));
    }
}
