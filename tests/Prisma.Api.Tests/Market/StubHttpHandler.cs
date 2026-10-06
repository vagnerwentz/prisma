using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Prisma.Api.Tests.Market;

// Responde no lugar do fornecedor: os testes nunca chamam a brapi de verdade (docs/investimentos.md, seção 8).
// Guarda os pedidos, para conferir endereço, consulta e cabeçalhos.
public sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<HttpRequestMessage> Requests => _requests.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Fixture(string fileName) =>
        Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Market", "Fixtures", fileName)));

    // Valor de um parâmetro da consulta (page=2 → "2").
    public static string? Query(HttpRequestMessage request, string name) =>
        request.RequestUri!.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == name)
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .FirstOrDefault();
}
