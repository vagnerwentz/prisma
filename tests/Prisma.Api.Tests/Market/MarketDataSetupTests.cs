using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Market.Bcb;
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

    [Fact]
    public async Task The_reference_rates_come_from_the_central_bank()
    {
        await using var factory = new PrismaApiFactory(NoDatabase);

        factory.Services.GetRequiredService<IReferenceRateSource>().ShouldBeOfType<BcbSgsRateSource>();
    }

    [Fact]
    public async Task The_central_bank_client_has_the_default_address_timeout_and_no_token()
    {
        await using var factory = new PrismaApiFactory(NoDatabase);

        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(BcbSgsRateSource.HttpClientName);

        client.BaseAddress.ShouldBe(new Uri("https://api.bcb.gov.br/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldStartWith("Prisma");
    }

    [Fact]
    public async Task The_central_bank_client_uses_the_configured_address_and_timeout()
    {
        await using var factory = new PrismaApiFactory(NoDatabase, new Dictionary<string, string>
        {
            ["Bcb:BaseUrl"] = "https://bcb.exemplo/",
            ["Bcb:TimeoutSeconds"] = "9",
        });

        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(BcbSgsRateSource.HttpClientName);

        client.BaseAddress.ShouldBe(new Uri("https://bcb.exemplo/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(9));
    }

    [Theory]
    [InlineData("Bcb:TimeoutSeconds", "0")]
    [InlineData("Bcb:TimeoutSeconds", "301")]
    public async Task Invalid_central_bank_configuration_stops_the_api_at_startup(string key, string value)
    {
        await using var factory = new PrismaApiFactory(NoDatabase, new Dictionary<string, string> { [key] = value });

        Should.Throw<OptionsValidationException>(() => factory.Services);
    }
}
