using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Market.Brapi;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// A brapi ligada na API de verdade: configuração, token e validação ao subir. Não precisa de banco nem
// chama a brapi (só monta o cliente).
public sealed class MarketDataSetupTests
{
    // Ninguém escuta na porta 1: estes testes não tocam no banco.
    private const string NoDatabase = "Host=localhost;Port=1;Database=prisma;Username=prisma;Password=x;Timeout=2";

    [Fact]
    public async Task The_asset_list_comes_from_brapi()
    {
        await using var factory = new PrismaApiFactory(NoDatabase);

        factory.Services.GetRequiredService<IAssetListSource>().ShouldBeOfType<BrapiAssetListSource>();
    }

    [Fact]
    public async Task Client_uses_the_configured_address_timeout_and_token_in_the_header()
    {
        await using var factory = new PrismaApiFactory(NoDatabase, new Dictionary<string, string>
        {
            ["Brapi:BaseUrl"] = "https://brapi.exemplo/",
            ["Brapi:TimeoutSeconds"] = "7",
            ["Brapi:Token"] = "token-de-teste",
        });

        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(BrapiAssetListSource.HttpClientName);

        client.BaseAddress.ShouldBe(new Uri("https://brapi.exemplo/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(7));
        client.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("token-de-teste");
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldStartWith("Prisma");
    }

    [Fact]
    public async Task Without_token_the_client_sends_no_authorization()
    {
        await using var factory = new PrismaApiFactory(NoDatabase);

        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(BrapiAssetListSource.HttpClientName);

        client.BaseAddress.ShouldBe(new Uri("https://brapi.dev/"));
        client.DefaultRequestHeaders.Authorization.ShouldBeNull();
    }

    [Theory]
    [InlineData("Brapi:PageSize", "5000")] // a brapi corta em 2.000 sem avisar
    [InlineData("Brapi:PageSize", "0")]
    [InlineData("Brapi:MaxPages", "0")]
    [InlineData("Brapi:TimeoutSeconds", "0")]
    public async Task Invalid_configuration_stops_the_api_at_startup(string key, string value)
    {
        await using var factory = new PrismaApiFactory(NoDatabase, new Dictionary<string, string> { [key] = value });

        Should.Throw<OptionsValidationException>(() => factory.Services);
    }
}
