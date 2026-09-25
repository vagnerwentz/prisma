using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa H.1 (PLAN.md): o que muda quando a API roda em produção, atrás do proxy da hospedagem e
// servindo o React no mesmo domínio.
[Collection(ApiCollection.Name)]
public sealed class HostingTests(PostgresFixture postgres) : IDisposable
{
    private const string IndexHtml = "<!doctype html><title>Prisma</title><div id=\"root\"></div>";

    // Um build de frontend de mentira: o index.html e um arquivo com hash em /assets.
    private readonly string _webRoot = Directory.CreateTempSubdirectory("prisma-web-").FullName;

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    private PrismaApiFactory FrontendFactory()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-B1a2c3.js"), "console.log('prisma')");
        return new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string> { [WebHostDefaults.WebRootKey] = _webRoot });
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@teste.com.br";

    [Theory]
    [InlineData("/")]
    [InlineData("/lancamentos")]
    [InlineData("/contas/0b7e3c1e-7a4f-4f59-9d38-2f0c2a7c1d11")]
    // Rota que também é da API: sem o /api, é tela, nunca o 401 da API.
    [InlineData("/accounts")]
    public async Task A_screen_route_returns_the_frontend_without_cache(string path)
    {
        await using var factory = FrontendFactory();
        using var client = factory.CreateRawClient();

        var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync()).ShouldBe(IndexHtml);
    }

    [Fact]
    public async Task Hashed_assets_are_cached_forever_and_a_missing_file_is_a_404()
    {
        await using var factory = FrontendFactory();
        using var client = factory.CreateRawClient();

        var asset = await client.GetAsync("/assets/index-B1a2c3.js");
        asset.StatusCode.ShouldBe(HttpStatusCode.OK);
        asset.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");

        // O pedaço de uma versão antiga não vira index.html.
        (await client.GetAsync("/assets/index-velho.js")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_api_answers_only_under_the_api_prefix()
    {
        await using var factory = FrontendFactory();
        using var client = factory.CreateRawClient();

        (await client.GetAsync("/api/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Com sessão (sem ela, toda rota da API responde 401 antes de revelar se existe).
        using var signedIn = await factory.CreateAuthenticatedClientAsync();
        var unknown = await signedIn.GetAsync("/nao-existe");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Behind_the_proxy_the_login_rate_limit_counts_each_client_ip()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, new Dictionary<string, string>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["RateLimiting:Auth:PermitLimit"] = "2",
        });
        using var client = factory.CreateHttpsClient();

        Task<HttpResponseMessage> LoginFrom(string forwardedFor)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { email = "ninguem@teste.com.br", password = "senha-errada-1" }),
            };
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            return client.SendAsync(request);
        }

        (await LoginFrom("203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginFrom("203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginFrom("203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Outro cliente atrás do mesmo proxy tem a própria cota.
        (await LoginFrom("203.0.113.20")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // O que o cliente escreve antes do IP acrescentado pelo proxy não conta: só o último vale.
        (await LoginFrom("198.51.100.99, 203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task With_registration_closed_only_an_allowed_email_signs_up()
    {
        var owner = NewEmail();
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, new Dictionary<string, string>
        {
            ["Registration:Open"] = "false",
            ["Registration:AllowedEmails:0"] = owner,
            ["RateLimiting:Auth:PermitLimit"] = "1000",
        });
        using var client = factory.CreateHttpsClient();

        var stranger = await client.PostAsJsonAsync("/auth/register",
            new { email = NewEmail(), password = PrismaApiFactory.Password });
        stranger.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe("O cadastro está fechado por enquanto.");

        var allowed = await client.PostAsJsonAsync("/auth/register",
            new { email = owner.ToUpperInvariant(), password = PrismaApiFactory.Password });
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_session_survives_a_restart_because_the_keys_live_in_the_database()
    {
        string cookie;
        await using (var first = new PrismaApiFactory(postgres.ConnectionString))
        {
            using var client = first.CreateRawClient();
            var registered = await client.PostAsJsonAsync("/api/auth/register",
                new { email = NewEmail(), password = PrismaApiFactory.Password });
            registered.StatusCode.ShouldBe(HttpStatusCode.Created);
            var setCookie = registered.Headers.GetValues("Set-Cookie").Single();
            // No caminho raiz, não no /api: substitui o cookie de antes do prefixo, em vez de conviver com ele.
            setCookie.ShouldContain("path=/;");
            cookie = setCookie.Split(';')[0];

            using var scope = first.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataProtectionKeys.CountAsync())
                .ShouldBeGreaterThan(0);
        }

        // Outro processo da API, como depois de um deploy: o mesmo cookie ainda vale.
        await using var second = new PrismaApiFactory(postgres.ConnectionString);
        using var again = second.CreateRawClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", cookie);

        (await again.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
