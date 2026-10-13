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

// docs/investimentos.md, "I.7c1": a curadoria dos eventos (arquivo) vira tabela; o ativo antigo entra inativo; os
// eventos valem sozinhos na carteira. O caso real do dono: BCFF11 → BTHF11.
[Collection(ApiCollection.Name)]
public sealed class CorporateEventsTests(PostgresFixture postgres)
{
    private sealed record AssetDto(Guid Id, string Symbol, bool IsActive, string? ConvertedToSymbol, string? ConvertedOn);

    private sealed record PositionDto(Guid AssetId, string Symbol, bool IsActive, long Quantity, long GrossCostCents, long? AveragePriceCents);

    private sealed record StepDto(
        string Kind, Guid? NoteId, string Date, string? Side, long Quantity, long QuantityAfter, string? RelatedSymbol, int? FromQuantity,
        int? ToQuantity);

    private sealed record ProblemDto(string? Detail);

    private sealed class FakeEventSource : ICorporateEventSource
    {
        public CorporateEventFile File { get; set; } = new([], []);

        public CorporateEventFile Load() => File;
    }

    private readonly FakeClock _clock = new(new DateTime(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _assets = new();
    private readonly FakeEventSource _events = new();

    private static readonly HistoricalAssetEntry Bcff11 = new("BCFF11", "BTG Pactual Fundo de Fundos FII", AssetKind.Fii, new DateOnly(2024, 10, 18));

    private static readonly CorporateEventEntry Split =
        new("bcff11-split", CorporateEventKind.Split, "BCFF11", null, new DateOnly(2023, 11, 28), 1, 8, "teste");

    private static readonly CorporateEventEntry Conversion =
        new("bcff11-bthf11", CorporateEventKind.Conversion, "BCFF11", "BTHF11", new DateOnly(2024, 12, 16), 100, 80, "teste");

    private static object Note(string date, Guid assetId, string side, long quantity, long price) => new
    {
        broker = "Íon", date, feesCents = 0, brokerageFeeCents = 0, brokerageFeeOnAllKinds = false,
        trades = new[] { new { assetId, side, quantity, unitPriceCents = price } },
    };

    [Fact]
    public async Task Sync_brings_the_old_asset_inactive_and_the_events()
    {
        var (factory, client) = await Start();
        await using var _ = factory;

        var bcff = await Asset(client, "BCFF11");

        bcff.IsActive.ShouldBeFalse();
        (bcff.ConvertedToSymbol, bcff.ConvertedOn).ShouldBe(("BTHF11", "2024-12-16"));
        (await Asset(client, "BTHF11")).ConvertedToSymbol.ShouldBeNull();
    }

    [Fact]
    public async Task Owner_case_ends_in_BTHF11_with_the_whole_cost()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;
        await Post(client, Note("2020-09-30", bcff, "Buy", 10, 9028));
        await Post(client, Note("2023-10-02", bcff, "Buy", 100, 7219));

        var positions = (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!;

        var bthf = positions.Single(p => p.Symbol == "BTHF11");
        (bthf.Quantity, bthf.GrossCostCents, bthf.AveragePriceCents).ShouldBe((704L, 812180L, (long?)1154));
        positions.Single(p => p.Symbol == "BCFF11").ShouldBe(new PositionDto(bcff, "BCFF11", false, 0, 0, null));

        var bcffSteps = (await client.GetFromJsonAsync<List<StepDto>>($"/positions/{bcff}"))!;
        bcffSteps.Select(s => (s.Kind, s.Quantity, s.QuantityAfter)).ShouldBe([
            ("Trade", 10L, 10L), ("Trade", 100L, 110L), ("Split", 110L, 880L), ("ConversionOut", 880L, 0L),
        ]);
        (bcffSteps[2].NoteId, bcffSteps[2].Side, bcffSteps[2].FromQuantity, bcffSteps[2].ToQuantity).ShouldBe(((Guid?)null, (string?)null, (int?)1, (int?)8));
        var inStep = (await client.GetFromJsonAsync<List<StepDto>>($"/positions/{bthf.AssetId}"))!.ShouldHaveSingleItem();
        (inStep.Kind, inStep.Quantity, inStep.RelatedSymbol, inStep.Date).ShouldBe(("ConversionIn", 704L, (string?)"BCFF11", "2024-12-16"));
    }

    [Fact]
    public async Task Note_of_the_old_asset_after_the_conversion_is_refused()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;

        var response = await client.PostAsJsonAsync("/trade-notes", Note("2026-10-06", bcff, "Buy", 100, 700));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe("BCFF11 virou BTHF11 em 16/12/2024. Desde então, lance como BTHF11.");
    }

    // A curadoria corrige um evento: a carteira acompanha na próxima leitura, porque é calculada.
    [Fact]
    public async Task Corrected_event_changes_the_position()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;
        await Post(client, Note("2023-10-02", bcff, "Buy", 100, 7219));

        _events.File = new([Bcff11], [Split, Conversion with { To = 79 }]);
        await Sync(factory);

        (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!.Single(p => p.Symbol == "BTHF11").Quantity.ShouldBe(632); // 800 × 0,79
    }

    [Fact]
    public async Task Sync_twice_changes_nothing_and_removed_events_leave()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        await Sync(factory);
        (await Asset(client, "BCFF11")).ConvertedToSymbol.ShouldBe("BTHF11");

        _events.File = new([Bcff11], [Split]);
        await Sync(factory);

        (await Asset(client, "BCFF11")).ConvertedToSymbol.ShouldBeNull();
        (await client.GetFromJsonAsync<List<AssetDto>>("/assets?q=BCFF11"))!.Count(a => a.Symbol == "BCFF11").ShouldBe(1);
    }

    // O destino ainda não está no catálogo: o evento fica de fora, o resto entra.
    [Fact]
    public async Task Event_with_an_unknown_asset_is_skipped()
    {
        _events.File = new([Bcff11], [Split, Conversion with { Target = "ZZZZ11" }]);
        var (factory, client) = await Start(keepFile: true);
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;
        await Post(client, Note("2023-10-02", bcff, "Buy", 100, 7219));

        (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!.Single().Quantity.ShouldBe(800);
        (await Asset(client, "BCFF11")).ConvertedToSymbol.ShouldBeNull();
    }

    // A tarefa diária roda os eventos logo depois do catálogo: o destino de uma conversão pode ter acabado de chegar.
    [Fact]
    public async Task The_daily_worker_syncs_the_events_after_the_catalog()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var logs = new LogSink();
        _assets.Assets = [Listed("BTHF11", AssetKind.Fii, "BTHF11", "BTG Pactual Real Estate Hedge Fund")];
        _events.File = new([Bcff11], [Split, Conversion]);
        await using var factory = new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string> { ["Assets:Sync:Enabled"] = "true" }, _clock, logs, services =>
            {
                services.AddScoped<IAssetListSource>(_ => _assets);
                services.AddSingleton<ICorporateEventSource>(_events);
            });
        _ = factory.Services; // sobe a API, e com ela a tarefa

        await logs.WaitFor(e => e.EventId.Name == "CorporateEventsSynced");

        var synced = logs.Entries.Single(e => e.EventId.Name == "CorporateEventsSynced");
        (synced["AssetsAdded"], synced["Added"]).ShouldBe((1, 2));
    }

    // docs/investimentos.md, "I.7c4": o BCFF11 foi liquidado e pago em BTHF11 a R$ 10,25, mais R$ 0,56 de caixa por cota.
    // A curadoria troca o tipo do evento, e a carteira acompanha na leitura seguinte. O caso do dono: 112 cotas a R$ 67,36
    // (R$ 7.544,32) → 896 no desdobramento → 716 BTHF11 a R$ 10,25; resultado +R$ 304,64 em 2024.
    private static readonly CorporateEventEntry Liquidation =
        Conversion with { Kind = CorporateEventKind.Liquidation, TargetUnitPrice = "10.25", CashPerUnit = "0.56" };

    private sealed record PortfolioDto(long InvestedCents, long RealizedCents);

    private sealed record ValueStepDto(string Kind, long Quantity, long GrossCents, long? RealizedCents, string? RelatedSymbol);

    [Fact]
    public async Task Liquidation_gives_the_reference_price_and_the_result_of_the_sale()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;
        await Post(client, Note("2023-03-10", bcff, "Buy", 112, 6736));
        (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!.Single(p => p.Symbol == "BTHF11").AveragePriceCents.ShouldBe(1054);

        _events.File = new([Bcff11], [Split, Liquidation]);
        await Sync(factory);

        var bthf = (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!.Single(p => p.Symbol == "BTHF11");
        (bthf.Quantity, bthf.GrossCostCents, bthf.AveragePriceCents).ShouldBe((716L, 733900L, (long?)1025));
        var bcffOut = (await client.GetFromJsonAsync<List<ValueStepDto>>($"/positions/{bcff}"))!.Last();
        bcffOut.ShouldBe(new ValueStepDto("LiquidationOut", 896, 784896, 30464, "BTHF11"));
        var bthfIn = (await client.GetFromJsonAsync<List<ValueStepDto>>($"/positions/{bthf.AssetId}"))!.Single();
        bthfIn.ShouldBe(new ValueStepDto("LiquidationIn", 716, 733900, null, "BCFF11"));
        (await client.GetFromJsonAsync<PortfolioDto>("/portfolio?year=2024"))!.ShouldBe(new PortfolioDto(733900, 30464));
        (await Asset(client, "BCFF11")).ConvertedToSymbol.ShouldBe("BTHF11");
    }

    // Valor por cota fora do formato recusa o arquivo inteiro: fica o que estava.
    [Fact]
    public async Task Invalid_unit_value_refuses_the_whole_file()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var bcff = (await Asset(client, "BCFF11")).Id;
        await Post(client, Note("2023-03-10", bcff, "Buy", 112, 6736));

        _events.File = new([Bcff11], [Split, Liquidation with { CashPerUnit = "0,56" }]);
        await Sync(factory);

        (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!.Single(p => p.Symbol == "BTHF11").AveragePriceCents.ShouldBe(1054);
    }

    // O arquivo de verdade, que vai na imagem: lê, sem chave repetida, e cada evento fecha (proporção e destino).
    [Fact]
    public void Embedded_file_is_valid()
    {
        var file = new EmbeddedCorporateEventSource().Load();

        file.Events.ShouldNotBeEmpty();
        file.Events.Select(e => e.Key).ShouldBeUnique();
        var ids = file.Events.SelectMany(e => new[] { e.Symbol, e.Target }).OfType<string>().Distinct().ToDictionary(s => s, _ => Guid.CreateVersion7());
        var definitions = file.Events
            .Select(e => new CorporateEventDefinition(e.Key, e.Kind, ids[e.Symbol], e.Target is null ? null : ids[e.Target], e.Date, e.From, e.To, e.Source,
                UnitValue(e.TargetUnitPrice, e.Key), UnitValue(e.CashPerUnit, e.Key)))
            .ToList();
        CorporateEventReconciliation.Apply([], definitions, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        foreach (var asset in file.HistoricalAssets)
            ListedAsset.TryCreate(asset.Symbol, asset.Name, asset.Name, asset.Kind, out _).ShouldBeTrue(asset.Symbol);
    }

    private async Task<(PrismaApiFactory, HttpClient)> Start(bool keepFile = false)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null, services =>
        {
            services.AddScoped<IAssetListSource>(_ => _assets);
            services.AddSingleton<ICorporateEventSource>(_events);
        });
        _assets.Assets = [Listed("BTHF11", AssetKind.Fii, "BTHF11", "BTG Pactual Real Estate Hedge Fund")];
        if (!keepFile)
            _events.File = new([Bcff11], [Split, Conversion]);
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        await Sync(factory);
        return (factory, await factory.CreateAuthenticatedClientAsync());
    }

    private static long? UnitValue(string? text, string key)
    {
        if (text is null)
            return null;
        CorporateEvent.TryParseUnitValue(text, out var value).ShouldBeTrue(key);
        return value;
    }

    private static Task Sync(PrismaApiFactory factory) => factory.Services.GetRequiredService<CorporateEventSync>().RunAsync(CancellationToken.None);

    private static async Task Post(HttpClient client, object note)
    {
        var response = await client.PostAsJsonAsync("/trade-notes", note);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<AssetDto> Asset(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.Single(a => a.Symbol == symbol);
}
