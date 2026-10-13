using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, "I.7c3": "Não encontrou? Avise a gente". O pedido é só o código; se o ativo chegou é
// calculado pelo catálogo, venha da brapi ou da curadoria; a fila da curadoria agrupa por código.
[Collection(ApiCollection.Name)]
public sealed class AssetRequestsTests(PostgresFixture postgres)
{
    private sealed record ArrivedDto(Guid Id, string Name, string Kind, bool HasLogo, bool IsActive);

    private sealed record RequestDto(Guid Id, string Symbol, string RequestedOn, string Status, ArrivedDto? Asset);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record ProblemDto(string? Detail);

    private sealed class FakeEventSource : ICorporateEventSource
    {
        public CorporateEventFile File { get; set; } = new([], []);

        public CorporateEventFile Load() => File;
    }

    // A consulta da fila, igual à do docs/operacao.md, seção 4.3.
    private const string QueueSql = """
        SELECT r.symbol, count(*) AS requests, min(r.created_at) AS first_requested_at
        FROM asset_requests r
        WHERE r.deleted_at IS NULL
          AND NOT EXISTS (SELECT 1 FROM assets a WHERE a.symbol = r.symbol)
        GROUP BY r.symbol
        ORDER BY requests DESC, first_requested_at;
        """;

    // 23h30 de 07/10 em São Paulo: o pedido é do dia 07, não do dia 08 em UTC.
    private readonly FakeClock _clock = new(new DateTime(2026, 10, 8, 2, 30, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _assets = new();
    private readonly FakeEventSource _events = new();

    private static readonly ListedAsset Itub3 = Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A.");

    private static readonly ListedAsset Pcip11 = Listed("PCIP11", AssetKind.Fii, "PCIP11", "Patria Crédito Imobiliário Índice de Preços");

    [Fact]
    public async Task Request_keeps_the_code_as_the_catalog_does()
    {
        var logs = new LogSink();
        var (factory, client) = await Start(logs);
        await using var _ = factory;

        var request = await Request(client, " cvbi11f ");

        (request.Symbol, request.RequestedOn, request.Status, request.Asset).ShouldBe(("CVBI11", "2026-10-07", "Waiting", (ArrivedDto?)null));
        (await List(client)).ShouldBe([request]);
        var line = await logs.WaitFor(l => l.EventId.Name == "AssetRequested");
        (line["Symbol"], line["RequestId"]).ShouldBe(("CVBI11", (object)request.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("VBI CRI")]
    [InlineData("CVBI111")]
    public async Task Anything_but_a_code_is_refused(string typed)
    {
        var (factory, client) = await Start();
        await using var _ = factory;

        var response = await client.PostAsJsonAsync("/asset-requests", new { symbol = typed });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldBe("Digite o código do ativo, como CVBI11.");
        (await List(client)).ShouldBeEmpty();
    }

    // Ativo ou inativo (o BCFF11 da curadoria saiu da bolsa), o código do catálogo se acha pela busca.
    [Theory]
    [InlineData("itub3", "ITUB3")]
    [InlineData("BCFF11", "BCFF11")]
    public async Task Code_already_in_the_catalog_is_refused(string typed, string symbol)
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        _events.File = new([new HistoricalAssetEntry("BCFF11", "BTG Pactual Fundo de Fundos FII", AssetKind.Fii, new DateOnly(2024, 10, 18))], []);
        await SyncEvents(factory);

        var response = await client.PostAsJsonAsync("/asset-requests", new { symbol = typed });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Detail(response)).ShouldBe($"{symbol} já está no Prisma. Busque de novo.");
    }

    [Fact]
    public async Task Asking_again_gives_back_the_same_request()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var first = await Request(client, "CVBI11");
        _clock.UtcNow = _clock.UtcNow.AddDays(3);

        var again = await Request(client, "cvbi11");

        again.ShouldBe(first);
        (await List(client)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task At_most_twenty_waiting_and_the_arrived_do_not_count()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        for (var i = 1; i <= 20; i++)
            await Request(client, $"ZZ{i:D2}11");

        var refused = await client.PostAsJsonAsync("/asset-requests", new { symbol = "CVBI11" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Detail(refused)).ShouldBe("Você tem 20 avisos esperando. Cancele um ou espere algum chegar.");

        await AddToCatalog(factory, Listed("ZZ0111"));
        (await Request(client, "CVBI11")).Status.ShouldBe("Waiting");
    }

    // O caso do CVBI11 pela brapi: chegou; depois da nota, o aviso sai da lista.
    [Fact]
    public async Task Request_arrives_from_the_catalog_and_leaves_after_a_note()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var request = await Request(client, "CVBI11");

        await AddToCatalog(factory, Listed("CVBI11", AssetKind.Fii, "CVBI11", "VBI CRI"));

        var arrived = (await List(client)).ShouldHaveSingleItem();
        var cvbi = await AssetId(client, "CVBI11");
        (arrived.Id, arrived.Status).ShouldBe((request.Id, "Arrived"));
        arrived.Asset.ShouldBe(new ArrivedDto(cvbi, "VBI CRI", "Fii", false, true));

        var note = await client.PostAsJsonAsync("/trade-notes", new
        {
            broker = "Íon", date = "2022-05-10", feesCents = 0, brokerageFeeCents = 0, brokerageFeeOnAllKinds = false,
            trades = new[] { new { assetId = cvbi, side = "Buy", quantity = 10, unitPriceCents = 9000 } },
        });
        note.StatusCode.ShouldBe(HttpStatusCode.Created, await note.Content.ReadAsStringAsync());
        (await List(client)).ShouldBeEmpty();
    }

    // O mesmo pela curadoria: o ativo antigo entra inativo, com o evento.
    [Fact]
    public async Task Request_arrives_from_the_curation_file()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        await Request(client, "CVBI11");

        _events.File = new(
            [new HistoricalAssetEntry("CVBI11", "VBI CRI", AssetKind.Fii, new DateOnly(2025, 9, 24))],
            [new CorporateEventEntry("cvbi11-pcip11", CorporateEventKind.Conversion, "CVBI11", "PCIP11", new DateOnly(2025, 9, 24), 1, 1, "teste")]);
        await SyncEvents(factory);

        var arrived = (await List(client)).ShouldHaveSingleItem();
        (arrived.Status, arrived.Asset!.IsActive).ShouldBe(("Arrived", false));
    }

    [Fact]
    public async Task Cancel_and_undo_keep_the_same_request()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var request = await Request(client, "CVBI11");

        (await client.DeleteAsync($"/asset-requests/{request.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(client)).ShouldBeEmpty();

        var restored = await client.PostAsync($"/asset-requests/{request.Id}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await restored.Content.ReadFromJsonAsync<RequestDto>()).ShouldBe(request);

        await client.DeleteAsync($"/asset-requests/{request.Id}");
        (await Request(client, "CVBI11")).Id.ShouldBe(request.Id);
        (await List(client)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Undo_respects_the_limit()
    {
        var (factory, client) = await Start();
        await using var _ = factory;
        var cancelled = await Request(client, "CVBI11");
        await client.DeleteAsync($"/asset-requests/{cancelled.Id}");
        for (var i = 1; i <= 20; i++)
            await Request(client, $"ZZ{i:D2}11");

        var restored = await client.PostAsync($"/asset-requests/{cancelled.Id}/restore", null);

        restored.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Cada um vê só os seus; a fila da curadoria conta todos, sem os cancelados e sem os que chegaram.
    [Fact]
    public async Task Each_person_sees_their_own_and_the_queue_counts_everyone()
    {
        var (factory, ana) = await Start();
        await using var _ = factory;
        var bruno = await factory.CreateAuthenticatedClientAsync();
        var anaCvbi = await Request(ana, "CVBI11");
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        await Request(bruno, "CVBI11");
        await Request(ana, "ABCD11");
        var cancelled = await Request(bruno, "WXYZ11");
        await bruno.DeleteAsync($"/asset-requests/{cancelled.Id}");
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        await Request(bruno, "PQRS11");
        await AddToCatalog(factory, Listed("PQRS11"));

        (await List(bruno)).Select(r => r.Symbol).ShouldBe(["PQRS11", "CVBI11"]);
        (await bruno.DeleteAsync($"/asset-requests/{anaCvbi.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PostAsync($"/asset-requests/{anaCvbi.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Queue()).ShouldBe([("CVBI11", 2L), ("ABCD11", 1L)]);
    }

    [Fact]
    public async Task Requests_are_rate_limited_per_person()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        await using var factory = new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string>
            {
                ["RateLimiting:Auth:PermitLimit"] = "1000",
                ["RateLimiting:AssetRequests:PermitLimit"] = "2",
            },
            _clock);
        var ana = await factory.CreateAuthenticatedClientAsync();
        var bruno = await factory.CreateAuthenticatedClientAsync();

        await Request(ana, "AAAA11");
        await Request(ana, "BBBB11");
        (await ana.PostAsJsonAsync("/asset-requests", new { symbol = "CCCC11" })).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await Request(bruno, "CCCC11")).Status.ShouldBe("Waiting");
    }

    private async Task<(PrismaApiFactory, HttpClient)> Start(LogSink? logs = null)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, logs, services =>
        {
            services.AddScoped<IAssetListSource>(_ => _assets);
            services.AddSingleton<ICorporateEventSource>(_events);
        });
        _assets.Assets = [Itub3, Pcip11];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        return (factory, await factory.CreateAuthenticatedClientAsync());
    }

    private async Task AddToCatalog(PrismaApiFactory factory, ListedAsset asset)
    {
        _assets.Assets = [.. _assets.Assets, asset];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
    }

    private static Task SyncEvents(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<CorporateEventSync>().RunAsync(CancellationToken.None);

    private static async Task<RequestDto> Request(HttpClient client, string symbol)
    {
        var response = await client.PostAsJsonAsync("/asset-requests", new { symbol });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestDto>())!;
    }

    private static async Task<List<RequestDto>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<RequestDto>>("/asset-requests"))!;

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.Single(a => a.Symbol == symbol).Id;

    private static async Task<string?> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail;

    private async Task<List<(string, long)>> Queue()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(QueueSql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, long)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        return rows;
    }
}
