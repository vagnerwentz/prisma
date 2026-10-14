using System.Net;
using System.Text.Json;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Email.Resend;
using Prisma.Api.Tests.Market;
using Shouldly;

namespace Prisma.Api.Tests.Email;

// O cliente do Resend com um HttpMessageHandler falso: o pedido que ele monta e o que faz de cada resposta. Os testes
// nunca chamam o Resend de verdade (ResendLiveTests, fora do CI).
public sealed class ResendEmailTransportTests
{
    private static readonly EmailMessage Message = new(
        "Prisma Money <nao-responda@prismamoney.com.br>",
        "contato@prismamoney.com.br",
        "pessoa@exemplo.com.br",
        "Confirme seu e-mail",
        "<p>Olá</p>",
        "Olá");

    private static (ResendEmailTransport Transport, StubHttpHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHttpHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.exemplo/") };
        return (new ResendEmailTransport(http), handler);
    }

    [Fact]
    public async Task Posts_the_message_in_the_resend_format()
    {
        string? body = null;
        var (transport, handler) = Create(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return StubHttpHandler.Json("""{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}""");
        });

        var result = await transport.SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Sent("49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"));
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri.ShouldBe(new Uri("https://api.resend.exemplo/emails"));
        request.Headers.Contains("Idempotency-Key").ShouldBeFalse();

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        root.GetProperty("from").GetString().ShouldBe("Prisma Money <nao-responda@prismamoney.com.br>");
        root.GetProperty("to").EnumerateArray().Select(e => e.GetString()).ShouldBe(["pessoa@exemplo.com.br"]);
        root.GetProperty("reply_to").EnumerateArray().Select(e => e.GetString()).ShouldBe(["contato@prismamoney.com.br"]);
        root.GetProperty("subject").GetString().ShouldBe("Confirme seu e-mail");
        root.GetProperty("html").GetString().ShouldBe("<p>Olá</p>");
        root.GetProperty("text").GetString().ShouldBe("Olá");
        root.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["from", "html", "reply_to", "subject", "text", "to"]);
    }

    [Theory]
    [InlineData(429, "daily_quota_exceeded")]
    [InlineData(429, "rate_limit_exceeded")]
    [InlineData(500, "application_error")]
    [InlineData(503, "service_unavailable")]
    [InlineData(401, "missing_api_key")] // configuração: conserta-se sem perder o e-mail
    [InlineData(403, "restricted_api_key")]
    [InlineData(409, "concurrent_idempotent_requests")]
    public async Task Transient_errors_are_retried(int status, string name)
    {
        var (transport, _) = Create(_ => StubHttpHandler.Json(
            $$"""{"statusCode":{{status}},"name":"{{name}}","message":"falhou para pessoa@exemplo.com.br"}""",
            (HttpStatusCode)status));

        var result = await transport.SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Retry($"http_{status}_{name}"));
    }

    [Theory]
    [InlineData(400, "validation_error")]
    [InlineData(422, "missing_required_field")]
    public async Task Invalid_requests_are_rejected_without_the_provider_message(int status, string name)
    {
        var (transport, _) = Create(_ => StubHttpHandler.Json(
            $$"""{"statusCode":{{status}},"name":"{{name}}","message":"Invalid `to` field: pessoa@exemplo"}""",
            (HttpStatusCode)status));

        var result = await transport.SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Reject($"http_{status}_{name}"));
        result.Reason!.ShouldNotContain("@");
    }

    [Fact]
    public async Task An_unreadable_error_body_keeps_only_the_status()
    {
        var (transport, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway</html>"),
        });

        var result = await transport.SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Retry("http_502"));
    }

    [Fact]
    public async Task Network_failure_is_retried()
    {
        var (transport, _) = Create(_ => throw new HttpRequestException("conexão recusada"));

        var result = await transport.SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Retry("network"));
    }

    [Fact]
    public async Task Timeout_is_retried()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return StubHttpHandler.Json("{}");
        });
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.exemplo/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var result = await new ResendEmailTransport(http).SendAsync(Message, CancellationToken.None);

        result.ShouldBe(EmailSendResult.Retry("timeout"));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_a_failure()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return StubHttpHandler.Json("{}");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.exemplo/") };
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new ResendEmailTransport(http).SendAsync(Message, stopping.Token));
    }
}
