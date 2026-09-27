using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prisma.Domain;

namespace Prisma.Api.Tests.Infrastructure;

public sealed class PrismaApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string>? settings = null,
    IClock? clock = null,
    LogSink? logs = null)
    : WebApplicationFactory<Program>
{
    public const string Password = "senha-forte-123";

    // Padrão dos testes: rate limit alto, para não interferir nos testes que não tratam dele.
    // Quem quiser os valores de produção passa um dicionário vazio.
    private static readonly Dictionary<string, string> DefaultSettings = new()
    {
        ["RateLimiting:Auth:PermitLimit"] = "1000",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);

        foreach (var (key, value) in settings ?? DefaultSettings)
            builder.UseSetting(key, value);

        if (clock is not null)
            builder.ConfigureTestServices(services => services.AddSingleton(clock));

        if (logs is not null)
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
    }

    // HTTPS porque o cookie de sessão é Secure: por HTTP o cliente não o reenviaria. Os testes
    // escrevem as rotas da API sem o prefixo (/auth/me); o cliente acrescenta o /api, como o
    // frontend faz.
    public HttpClient CreateHttpsClient() =>
        CreateDefaultClient(new Uri("https://localhost"), new ApiPrefixHandler(), new CookieContainerHandler());

    // Cliente sem prefixo nem cookies: para os testes do próprio endereço (frontend, /api).
    public HttpClient CreateRawClient() => CreateDefaultClient(new Uri("https://localhost"));

    private sealed class ApiPrefixHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = new UriBuilder(request.RequestUri!);
            uri.Path = "/api" + uri.Path;
            request.RequestUri = uri.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }

    // Cliente já logado como um usuário novo.
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/auth/register",
            new { email = $"{Guid.NewGuid():N}@teste.com.br", password = Password });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
