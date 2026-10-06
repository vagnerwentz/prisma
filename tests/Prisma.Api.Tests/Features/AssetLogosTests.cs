using System.Net;
using System.Net.Http.Headers;
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

// docs/investimentos.md, etapa 4: os logos baixados, limpos, guardados e servidos pelo Prisma.
[Collection(ApiCollection.Name)]
public sealed class AssetLogosTests(PostgresFixture postgres)
{
    private const string BbasUrl = "https://icons.example/BBAS3.svg";
    private const string ItsaUrl = "https://icons.example/ITSA4.svg";

    private sealed record AssetDto(Guid Id, string Symbol, bool HasLogo);

    private sealed record HoldingDto(Guid Id, string Symbol, bool HasLogo);

    private readonly FakeAssetListSource _source = new();
    private readonly FakeAssetLogoDownloader _logos = new();
    private readonly LogSink _logs = new();

    [Fact]
    public async Task Logos_are_downloaded_cleaned_and_served_by_the_api()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D").Replace("<path", """<script>alert(1)</script><path onclick="x()" """);
        _logos.Svgs[ItsaUrl] = FakeAssetLogoDownloader.Square("#00488E");

        var summary = await Logos(factory);
        var client = await factory.CreateAuthenticatedClientAsync();
        var bbas = await Asset(client, "BBAS3");

        (summary.Pending, summary.Saved, summary.Failed).ShouldBe((2, 2, 0));
        var response = await client.GetAsync($"/assets/{bbas.Id}/logo");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        var svg = await response.Content.ReadAsStringAsync();
        svg.ShouldContain("#FFF22D");
        svg.ShouldNotContain("script");
        svg.ShouldNotContain("onclick");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(GetAssetLogo.ContentSecurityPolicy);
        response.Headers.ETag.ShouldNotBeNull();
        // O fundo, sem logo no fornecedor, nunca foi pedido.
        _logos.Requested.ShouldBe([BbasUrl, ItsaUrl], ignoreOrder: true);
    }

    [Fact]
    public async Task Same_logo_is_not_sent_again_when_the_browser_has_it()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D");
        await Logos(factory);
        var client = await factory.CreateAuthenticatedClientAsync();
        var bbas = await Asset(client, "BBAS3");
        var etag = (await client.GetAsync($"/assets/{bbas.Id}/logo")).Headers.ETag!;

        var again = new HttpRequestMessage(HttpMethod.Get, $"/assets/{bbas.Id}/logo");
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag));
        (await client.SendAsync(again)).StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Asset_without_a_logo_is_marked_and_has_no_logo_route()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D");
        await Logos(factory);
        var client = await factory.CreateAuthenticatedClientAsync();

        var mxrf = await Asset(client, "MXRF11");
        var bbas = await Asset(client, "BBAS3");

        (bbas.HasLogo, mxrf.HasLogo).ShouldBe((true, false));
        (await client.GetAsync($"/assets/{mxrf.Id}/logo")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/holdings", new { assetId = bbas.Id })).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!.Single().HasLogo.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failing_logo_does_not_stop_the_others_and_is_tried_again_next_run()
    {
        await using var factory = await Seeded();
        _logos.Svgs[ItsaUrl] = FakeAssetLogoDownloader.Square("#00488E");

        var first = await Logos(factory);
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D");
        var second = await Logos(factory);

        (first.Saved, first.Failed).ShouldBe((1, 1));
        _logs.Entries.ShouldContain(e => e.EventId.Name == "AssetLogoSkipped" && (string?)e["Symbol"] == "BBAS3");
        (second.Pending, second.Saved).ShouldBe((1, 1));
        var third = await Logos(factory);
        third.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task What_is_not_a_clean_svg_is_not_kept()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = "<html><body>404</body></html>";
        _logos.Svgs[ItsaUrl] = FakeAssetLogoDownloader.Square("#00488E");

        var summary = await Logos(factory);
        var client = await factory.CreateAuthenticatedClientAsync();

        (summary.Saved, summary.Rejected).ShouldBe((1, 1));
        (await Asset(client, "BBAS3")).HasLogo.ShouldBeFalse();
    }

    [Fact]
    public async Task New_address_downloads_again_and_a_lost_logo_is_removed()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D");
        _logos.Svgs[ItsaUrl] = FakeAssetLogoDownloader.Square("#00488E");
        await Logos(factory);

        const string newBbas = "https://icons.example/v2/BBAS3.svg";
        _logos.Svgs[newBbas] = FakeAssetLogoDownloader.Square("#123456");
        _source.Assets = [Listed("BBAS3", logoUrl: newBbas), Listed("ITSA4"), Listed("MXRF11", AssetKind.Fii)];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var summary = await Logos(factory);
        var client = await factory.CreateAuthenticatedClientAsync();

        (summary.Saved, summary.Removed).ShouldBe((1, 1));
        (await client.GetStringAsync($"/assets/{(await Asset(client, "BBAS3")).Id}/logo")).ShouldContain("#123456");
        (await Asset(client, "ITSA4")).HasLogo.ShouldBeFalse();
    }

    [Fact]
    public async Task Logo_requires_login()
    {
        await using var factory = await Seeded();
        _logos.Svgs[BbasUrl] = FakeAssetLogoDownloader.Square("#FFF22D");
        await Logos(factory);
        var bbas = await Asset(await factory.CreateAuthenticatedClientAsync(), "BBAS3");

        (await factory.CreateHttpsClient().GetAsync($"/assets/{bbas.Id}/logo")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<PrismaApiFactory> Seeded()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, null, _logs, services =>
        {
            services.AddScoped<IAssetListSource>(_ => _source);
            services.AddScoped<IAssetLogoDownloader>(_ => _logos);
        });
        _source.Assets =
        [
            Listed("BBAS3", logoUrl: BbasUrl),
            Listed("ITSA4", logoUrl: ItsaUrl),
            Listed("MXRF11", AssetKind.Fii), // fundo sem logo no fornecedor
        ];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        return factory;
    }

    private static Task<AssetLogoRunSummary> Logos(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<AssetLogoSync>().RunAsync(CancellationToken.None);

    private static async Task<AssetDto> Asset(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol);
}
