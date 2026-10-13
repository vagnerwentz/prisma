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

// docs/investimentos.md, "I.7c2": a pessoa confere o que cada evento fez com a quantidade dela. O caso do dono: 112
// BCFF11 → 896 no desdobramento → 716 BTHF11 na liquidação.
[Collection(ApiCollection.Name)]
public sealed class EventReviewsTests(PostgresFixture postgres)
{
    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record ItemDto(
        Guid EventId, string Kind, string Date, string Symbol, string? TargetSymbol, int FromQuantity, int ToQuantity, long QuantityBefore,
        long QuantityAfter, long? TargetUnitPriceCents);

    private sealed record ProblemDto(string? Detail);

    private sealed class FakeEventSource : ICorporateEventSource
    {
        public CorporateEventFile File { get; set; } = new([], []);

        public CorporateEventFile Load() => File;
    }

    private readonly FakeClock _clock = new(new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _assets = new();
    private readonly FakeEventSource _events = new();

    private static readonly HistoricalAssetEntry Bcff11 = new("BCFF11", "BTG Pactual Fundo de Fundos FII", AssetKind.Fii, new DateOnly(2024, 10, 18));

    private static readonly CorporateEventEntry Split =
        new("bcff11-split", CorporateEventKind.Split, "BCFF11", null, new DateOnly(2023, 11, 28), 1, 8, "teste");

    private static readonly CorporateEventEntry Liquidation =
        new("bcff11-bthf11", CorporateEventKind.Liquidation, "BCFF11", "BTHF11", new DateOnly(2024, 12, 16), 100, 80, "teste", "10.25", "0.56");

    [Fact]
    public async Task List_shows_what_each_event_did_to_the_quantity() =>
        await WithOwner(async (client, _) =>
        {
            var items = await Pending(client);

            items.Select(i => (i.Kind, i.Date, i.Symbol, i.TargetSymbol, i.QuantityBefore, i.QuantityAfter, i.TargetUnitPriceCents)).ShouldBe(
            [
                ("Split", "2023-11-28", "BCFF11", (string?)null, 112L, 896L, (long?)null),
                ("Liquidation", "2024-12-16", "BCFF11", "BTHF11", 896L, 716L, 1025L),
            ]);
            (items[0].FromQuantity, items[0].ToQuantity).ShouldBe((1, 8));
        });

    [Fact]
    public async Task Confirm_one_then_the_rest_and_undo() =>
        await WithOwner(async (client, _) =>
        {
            var items = await Pending(client);

            await Confirm(client, items[0].EventId);
            (await Pending(client)).Select(i => i.Kind).ShouldBe(["Liquidation"]);
            await Confirm(client, items[1].EventId);
            (await Pending(client)).ShouldBeEmpty();

            (await client.DeleteAsync($"/event-reviews/{items[1].EventId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Pending(client)).Select(i => i.Kind).ShouldBe(["Liquidation"]);
            // Conferir de novo devolve o mesmo registro.
            await Confirm(client, items[1].EventId);
            (await Reviews(items[1].EventId)).ShouldBe([("Confirmed", (long?)null, false)]);
        });

    [Fact]
    public async Task Confirm_all_that_the_list_shows() =>
        await WithOwner(async (client, _) =>
        {
            var items = await Pending(client);

            await Confirm(client, [.. items.Select(i => i.EventId)]);

            (await Pending(client)).ShouldBeEmpty();
        });

    [Fact]
    public async Task Dispute_records_the_quantity_and_leaves_the_list() =>
        await WithOwner(async (client, _) =>
        {
            var liquidation = (await Pending(client)).Single(i => i.Kind == "Liquidation");

            var disputed = await client.PostAsJsonAsync($"/event-reviews/{liquidation.EventId}/dispute", new { reportedQuantity = 704 });

            disputed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Pending(client)).Select(i => i.Kind).ShouldBe(["Split"]);
            (await Reviews(liquidation.EventId)).ShouldBe([("Disputed", (long?)704, false)]);

            var split = (await Pending(client)).Single();
            (await client.PostAsJsonAsync($"/event-reviews/{split.EventId}/dispute", new { reportedQuantity = (long?)null }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Reviews(split.EventId)).ShouldBe([("Disputed", (long?)null, false)]);
        });

    // A consulta da curadoria (docs/operacao.md, 4.4): os "Não confere" da versão atual de cada evento. Mudou uma, muda a
    // outra.
    public const string DisputesSql = """
        SELECT e.key, a.symbol, count(*) AS people, array_agg(r.reported_quantity ORDER BY r.created_at) AS reported
        FROM event_reviews r
        JOIN corporate_events e ON e.id = r.corporate_event_id
        JOIN assets a ON a.id = e.asset_id
        WHERE r.verdict = 'Disputed' AND r.deleted_at IS NULL AND r.event_version = e.updated_at
        GROUP BY e.key, a.symbol
        ORDER BY people DESC, e.key
        """;

    [Fact]
    public async Task Curation_sees_the_disputes() =>
        await WithOwner(async (client, factory) =>
        {
            var liquidation = (await Pending(client)).Single(i => i.Kind == "Liquidation");
            (await client.PostAsJsonAsync($"/event-reviews/{liquidation.EventId}/dispute", new { reportedQuantity = 704 }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var other = await factory.CreateAuthenticatedClientAsync();
            (await other.GetAsync("/event-reviews")).StatusCode.ShouldBe(HttpStatusCode.OK);

            await using var connection = new NpgsqlConnection(postgres.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(DisputesSql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue();
            (reader.GetString(0), reader.GetString(1), reader.GetInt64(2)).ShouldBe(("bcff11-bthf11", "BCFF11", 1L));
            reader.GetFieldValue<long?[]>(3).ShouldBe([704]);
            (await reader.ReadAsync()).ShouldBeFalse();
        });

    [Fact]
    public async Task Dispute_refuses_a_quantity_out_of_range() =>
        await WithOwner(async (client, _) =>
        {
            var item = (await Pending(client))[0];

            var refused = await client.PostAsJsonAsync($"/event-reviews/{item.EventId}/dispute", new { reportedQuantity = -1 });

            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe("A quantidade vai de 0 a 100.000.000.");
            (await Pending(client)).Count.ShouldBe(2);
        });

    // A curadoria corrige o evento: ele volta a pedir conferência, também para quem já tinha conferido.
    [Fact]
    public async Task Corrected_event_comes_back() =>
        await WithOwner(async (client, factory) =>
        {
            var items = await Pending(client);
            await Confirm(client, [.. items.Select(i => i.EventId)]);

            _events.File = new([Bcff11], [Split, Liquidation with { TargetUnitPrice = "10.30" }]);
            _clock.UtcNow = _clock.UtcNow.AddHours(1); // a correção chega depois
            await Sync(factory);

            var back = (await Pending(client)).Single();
            (back.Kind, back.TargetUnitPriceCents).ShouldBe(("Liquidation", (long?)1030));
        });

    // O evento que sai do arquivo leva as conferências junto.
    [Fact]
    public async Task Removed_event_takes_its_reviews() =>
        await WithOwner(async (client, factory) =>
        {
            var liquidation = (await Pending(client)).Single(i => i.Kind == "Liquidation");
            await Confirm(client, liquidation.EventId);

            _events.File = new([Bcff11], [Split]);
            await Sync(factory);

            (await Reviews(liquidation.EventId)).ShouldBeEmpty();
            (await Pending(client)).Select(i => i.Kind).ShouldBe(["Split"]);
        });

    // Quem não tem o ativo não fica sabendo do evento nem consegue conferi-lo; e não vê a conferência de outra pessoa.
    [Fact]
    public async Task Event_outside_the_portfolio_is_not_found() =>
        await WithOwner(async (client, factory) =>
        {
            var items = await Pending(client);
            await Confirm(client, items[0].EventId);
            var other = await factory.CreateAuthenticatedClientAsync();

            (await Pending(other)).ShouldBeEmpty();
            var refused = await other.PostAsJsonAsync("/event-reviews/confirm", new { eventIds = new[] { items[1].EventId } });
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await refused.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe("Evento não encontrado.");
            (await other.DeleteAsync($"/event-reviews/{items[0].EventId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Pending(client)).Count.ShouldBe(1);
        });

    // Um evento desconhecido no meio da lista: nada é gravado.
    [Fact]
    public async Task One_unknown_event_saves_nothing() =>
        await WithOwner(async (client, _) =>
        {
            var items = await Pending(client);

            var refused = await client.PostAsJsonAsync("/event-reviews/confirm", new { eventIds = new[] { items[0].EventId, Guid.CreateVersion7() } });

            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Pending(client)).Count.ShouldBe(2);
        });

    [Fact]
    public async Task Confirm_needs_one_to_two_hundred_events() =>
        await WithOwner(async (client, _) =>
        {
            (await client.PostAsJsonAsync("/event-reviews/confirm", new { eventIds = Array.Empty<Guid>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.CreateVersion7()).ToArray();
            (await client.PostAsJsonAsync("/event-reviews/confirm", new { eventIds = tooMany })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        });

    [Fact]
    public async Task Login_is_required()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        (await factory.CreateHttpsClient().GetAsync("/event-reviews")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task WithOwner(Func<HttpClient, PrismaApiFactory, Task> test)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null, services =>
        {
            services.AddScoped<IAssetListSource>(_ => _assets);
            services.AddSingleton<ICorporateEventSource>(_events);
        });
        _assets.Assets = [Listed("BTHF11", AssetKind.Fii, "BTHF11", "BTG Pactual Real Estate Hedge Fund")];
        _events.File = new([Bcff11], [Split, Liquidation]);
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        await Sync(factory);
        var client = await factory.CreateAuthenticatedClientAsync();
        var bcff = (await client.GetFromJsonAsync<List<AssetDto>>("/assets?q=BCFF11"))!.Single(a => a.Symbol == "BCFF11").Id;
        var note = await client.PostAsJsonAsync("/trade-notes", new
        {
            broker = "Íon", date = "2023-03-10", feesCents = 0, brokerageFeeCents = 0,
            trades = new[] { new { assetId = bcff, side = "Buy", quantity = 112, unitPriceCents = 6736 } },
        });
        note.StatusCode.ShouldBe(HttpStatusCode.Created);
        await test(client, factory);
    }

    private static Task Sync(PrismaApiFactory factory) => factory.Services.GetRequiredService<CorporateEventSync>().RunAsync(CancellationToken.None);

    private static async Task<List<ItemDto>> Pending(HttpClient client)
    {
        var response = await client.GetAsync("/event-reviews");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<ItemDto>>())!;
    }

    private static async Task Confirm(HttpClient client, params Guid[] ids)
    {
        var response = await client.PostAsJsonAsync("/event-reviews/confirm", new { eventIds = ids });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    // As conferências gravadas de um evento: veredito, quantidade e se está desfeita.
    private async Task<List<(string, long?, bool)>> Reviews(Guid eventId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT verdict, reported_quantity, deleted_at IS NOT NULL FROM event_reviews WHERE corporate_event_id = @id", connection);
        command.Parameters.AddWithValue("id", eventId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, long?, bool)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetBoolean(2)));
        return rows;
    }
}
