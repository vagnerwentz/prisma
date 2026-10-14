using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prisma.Api.Infrastructure.Email.Resend;

// Envio pelo Resend (POST /emails; docs/email.md). Nunca lança por falha do Resend: devolve Retry ou Reject, com um
// código curto como motivo. A mensagem de erro do Resend fica de fora, porque pode citar o endereço.
//
// Sem Idempotency-Key: o conteúdo muda a cada tentativa (o token do link é gerado na hora), e o Resend recusa a mesma
// chave com outro conteúdo. O preço é raro e inofensivo: se a resposta se perder depois do envio (tempo esgotado, a
// API cair), a pessoa recebe dois e-mails com links que valem os dois.
public sealed class ResendEmailTransport(HttpClient http) : IEmailTransport
{
    public const string HttpClientName = "resend";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var body = new SendRequest(message.From, [message.To], message.Subject, message.Html, message.Text, [message.ReplyTo]);
        try
        {
            using var response = await http.PostAsJsonAsync("emails", body, Json, ct);
            if (response.IsSuccessStatusCode)
            {
                var sent = await ReadAsync<SendResponse>(response, ct);
                return EmailSendResult.Sent(sent?.Id);
            }

            var error = await ReadAsync<ErrorResponse>(response, ct);
            var reason = error?.Name is { Length: > 0 } name
                ? $"http_{(int)response.StatusCode}_{name}"
                : $"http_{(int)response.StatusCode}";
            return IsTransient(response.StatusCode) ? EmailSendResult.Retry(reason) : EmailSendResult.Reject(reason);
        }
        catch (HttpRequestException)
        {
            return EmailSendResult.Retry("network");
        }
        // O HttpClient sinaliza o próprio timeout como cancelamento; o cancelamento de quem chamou passa adiante.
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return EmailSendResult.Retry("timeout");
        }
    }

    // 401 e 403 são chave errada, vencida ou sem permissão, ou domínio ainda não verificado: erro de configuração, que se
    // conserta sem mexer no e-mail. A fila espera (até 24 h) em vez de perder o que foi pedido nesse meio-tempo. 409 é
    // recurso ocupado; 429, limite do Resend (por segundo, por dia ou por mês).
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout
            or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    // Corpo ilegível não muda a decisão: sem ele, o motivo fica só com o status.
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record SendRequest(
        string From,
        string[] To,
        string Subject,
        string Html,
        string Text,
        [property: JsonPropertyName("reply_to")] string[] ReplyTo);

    private sealed record SendResponse(string? Id);

    private sealed record ErrorResponse(string? Name);
}
