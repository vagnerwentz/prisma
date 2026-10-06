using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, seção 8, etapa 3: GET /assets?q=, a busca para escolher um ativo. O catálogo é global:
// cada teste o recria pela sincronização, com a fonte falsa.
[Collection(ApiCollection.Name)]
public sealed class SearchAssetsTests(PostgresFixture postgres)
{
    private sealed record ItemDto(Guid Id, string Symbol, string Name, string Kind, bool IsActive);

    private static readonly IReadOnlyList<ListedAsset> Catalog =
    [
        Listed("BBAS3", AssetKind.Stock, "BCO BRASIL S.A.", "Banco do Brasil S.A."),
        Listed("ITSA4", AssetKind.Stock, "ITAUSA S.A."),
        Listed("ITSA3", AssetKind.Stock, "ITAUSA S.A."),
        Listed("RITS3", AssetKind.Stock, "RITSA HOLDING"), // contém "itsa" no nome, sem começar por ele
        Listed("TAEE11", AssetKind.Unit, "TRANSMISSORA ALIANÇA DE ENERGIA ELÉTRICA S.A."),
        Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda Fundo de Investimento Imobiliario"),
        Listed("BOVA11", AssetKind.Etf, "BOVA11", "iShares Ibovespa Fundo de Indice"),
    ];

    private readonly FakeAssetListSource _source = new();

    [Fact]
    public async Task Exact_symbol_comes_first_then_symbols_that_start_with_the_term()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        (await Search(client, "itsa")).Select(i => i.Symbol).ShouldBe(["ITSA3", "ITSA4", "RITS3"]);
        (await Search(client, "itsa4")).Select(i => i.Symbol).ShouldBe(["ITSA4"]);
        (await Search(client, "BBAS3")).Single().Symbol.ShouldBe("BBAS3");
    }

    [Theory]
    [InlineData("alianca")]
    [InlineData("ALIANÇA")]
    [InlineData("energia eletrica")]
    [InlineData("Elétrica  transmissora")] // fora de ordem e com espaço a mais
    public async Task Finds_by_name_ignoring_case_accents_and_word_order(string q)
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        (await Search(client, q)).Single().Symbol.ShouldBe("TAEE11");
    }

    [Fact]
    public async Task Funds_are_found_and_shown_by_their_long_name()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        var fii = (await Search(client, "maxi renda")).Single();
        fii.Symbol.ShouldBe("MXRF11");
        fii.Name.ShouldBe("Maxi Renda Fundo de Investimento Imobiliario");
        fii.Kind.ShouldBe("Fii");
        (await Search(client, "banco do brasil")).Single().Name.ShouldBe("BCO BRASIL S.A.");
    }

    [Fact]
    public async Task Asset_that_left_the_exchange_still_appears_after_the_traded_ones()
    {
        await using var factory = await Seeded();
        _source.Assets = Catalog.Where(a => a.Symbol != "ITSA3").ToList();
        await Sync(factory);
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await Search(client, "itsa");

        items.Select(i => (i.Symbol, i.IsActive)).ShouldBe([("ITSA4", true), ("ITSA3", false), ("RITS3", true)]);
    }

    [Fact]
    public async Task Exact_symbol_comes_first_even_when_it_left_the_exchange()
    {
        // BOVA3 saiu da bolsa; BOVA31 (um BDR, por exemplo) continua e também começa por "bova3".
        await using var factory = await Seeded([Listed("BOVA3"), Listed("BOVA31", AssetKind.Bdr), Listed("BOVA11", AssetKind.Etf)]);
        _source.Assets = [Listed("BOVA31", AssetKind.Bdr), Listed("BOVA11", AssetKind.Etf)];
        await Sync(factory);
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await Search(client, "bova3");

        items.Select(i => (i.Symbol, i.IsActive)).ShouldBe([("BOVA3", false), ("BOVA31", true)]);
        (await Search(client, "bova")).Select(i => i.Symbol).ShouldBe(["BOVA11", "BOVA31", "BOVA3"]);
    }

    [Fact]
    public async Task Fractional_symbol_finds_the_standard_lot()
    {
        await using var factory = await Seeded([Listed("ITSA4"), Listed("ITSA4F")]);
        var client = await factory.CreateAuthenticatedClientAsync();

        (await Search(client, "itsa4")).Select(i => i.Symbol).ShouldBe(["ITSA4"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("zzzz")]
    [InlineData("_")] // o curinga do LIKE vale como texto
    [InlineData("%")]
    public async Task Blank_or_unknown_term_finds_nothing(string? q)
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        (await Search(client, q)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_at_most_the_limit()
    {
        await using var factory = await Seeded(Enumerable.Range(1, 30).Select(n => Listed($"AB{n:D2}3")).ToList());
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await Search(client, "ab");

        items.Count.ShouldBe(SearchAssets.MaxResults);
        items.First().Symbol.ShouldBe("AB013");
    }

    [Fact]
    public async Task The_catalog_is_the_same_for_every_user()
    {
        await using var factory = await Seeded();
        var ana = await factory.CreateAuthenticatedClientAsync();
        var bia = await factory.CreateAuthenticatedClientAsync();

        (await Search(bia, "bbas3")).Single().Id.ShouldBe((await Search(ana, "bbas3")).Single().Id);
    }

    [Fact]
    public async Task Requires_login()
    {
        await using var factory = await Seeded();
        var anonymous = factory.CreateHttpsClient();

        (await anonymous.GetAsync("/assets?q=bbas3")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<PrismaApiFactory> Seeded(IReadOnlyList<ListedAsset>? assets = null)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);

        var factory = new PrismaApiFactory(postgres.ConnectionString, null, null, null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets = assets ?? Catalog;
        await Sync(factory);
        return factory;
    }

    private static Task Sync(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);

    private static async Task<List<ItemDto>> Search(HttpClient client, string? q)
    {
        var url = q is null ? "/assets" : $"/assets?q={Uri.EscapeDataString(q)}";
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ItemDto>>())!;
    }
}
