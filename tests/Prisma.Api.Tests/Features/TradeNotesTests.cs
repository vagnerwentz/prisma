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

// docs/investimentos.md, "I.7b1 API": a nota digitada, gravada; a carteira calculada das notas.
[Collection(ApiCollection.Name)]
public sealed class TradeNotesTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record LineDto(
        Guid AssetId, string Symbol, string AssetName, string AssetKind, bool AssetHasLogo, string Side, long Quantity,
        long UnitPriceCents, long GrossCents, long CostsCents);

    private sealed record NoteDto(
        Guid Id, string Broker, string Date, long FeesCents, long BrokerageFeeCents, bool BrokerageFeeOnAllKinds, List<LineDto> Trades);

    private sealed record PositionDto(
        Guid AssetId, string Symbol, long Quantity, long GrossCostCents, long CostBasisCents, long? AveragePriceCents,
        long? AverageCostCents, long RealizedCents);

    private sealed record StepDto(
        Guid NoteId, string Date, string Broker, string Side, long Quantity, long GrossCents, long CostsCents,
        long QuantityAfter, long? AveragePriceCents, long? AverageCostCents, long? RealizedCents);

    private sealed record HoldingDto(Guid Id, string Symbol);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents);

    private sealed record SummaryDto(long IncomeCents, long ExpenseCents);

    private sealed record ProblemDto(string? Detail);

    private readonly FakeClock _clock = new(new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _source = new();

    private sealed record Assets(Guid Itub3, Guid Bbse3, Guid Mxrf11);

    private static object Line(Guid assetId, string side, long quantity, long unitPriceCents) =>
        new { assetId, side, quantity, unitPriceCents };

    private static object Body(string date, object[] trades, long feesCents = 0, long brokerageFeeCents = 0,
        bool brokerageFeeOnAllKinds = false, string broker = "XP") =>
        new { broker, date, feesCents, brokerageFeeCents, brokerageFeeOnAllKinds, trades };

    [Fact]
    public async Task Note_gives_the_position_with_both_average_prices()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;

        var note = await Create(client, Body("2024-03-10",
            [Line(a.Itub3, "Buy", 50, 2000), Line(a.Itub3, "Buy", 30, 2010), Line(a.Bbse3, "Buy", 20, 3200)], brokerageFeeCents: 1000));

        (note.Broker, note.Date, note.BrokerageFeeCents).ShouldBe(("XP", "2024-03-10", 1000L));
        note.Trades.Select(t => (t.Symbol, t.Side, t.Quantity, t.GrossCents, t.CostsCents))
            .ShouldBe([("ITUB3", "Buy", 50L, 100000L, 312L), ("ITUB3", "Buy", 30L, 60300L, 188L), ("BBSE3", "Buy", 20L, 64000L, 500L)]);
        var itub3 = (await Positions(client)).Single(p => p.Symbol == "ITUB3");
        itub3.ShouldBe(new PositionDto(a.Itub3, "ITUB3", 80, 160300, 160800, 2004, 2010, 0));
    }

    // A carteira não é caixa (decisão do dono): a nota não mexe em saldo nem em "Entrou"/"Saiu".
    [Fact]
    public async Task Note_does_not_touch_balances_or_the_month()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var ion = (await (await client.PostAsJsonAsync("/accounts", new { name = "Íon", type = "Investment", initialBalanceCents = 500000 }))
            .Content.ReadFromJsonAsync<IdDto>())!.Id;

        await Create(client, Body("2026-10-01", [Line(a.Itub3, "Buy", 100, 2000)], feesCents: 100));
        await Create(client, Body("2026-10-02", [Line(a.Itub3, "Sell", 50, 2500)], feesCents: 100));

        (await client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == ion).BalanceCents.ShouldBe(500000);
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-10"))!.ShouldBe(new SummaryDto(0, 0));
    }

    [Fact]
    public async Task Selling_more_than_held_is_refused_and_nothing_is_saved()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));

        var response = await client.PostAsJsonAsync("/trade-notes", Body("2024-03-11", [Line(a.Itub3, "Sell", 110, 2500)]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldBe("Venda de 110 ITUB3 em 11/03/2024, mas a carteira tinha 100.");
        (await Notes(client)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_buy_that_a_later_sale_needs_is_refused()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var buy = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));
        var sell = await Create(client, Body("2025-02-12", [Line(a.Itub3, "Sell", 30, 2500)]));

        var refused = await client.DeleteAsync($"/trade-notes/{buy.Id}");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(refused)).ShouldBe("Venda de 30 ITUB3 em 12/02/2025, mas a carteira tinha 0.");
        (await client.DeleteAsync($"/trade-notes/{sell.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/trade-notes/{buy.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Positions(client)).ShouldBeEmpty();
        (await Notes(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Restoring_checks_the_portfolio_too()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var buy = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));
        var sell = await Create(client, Body("2025-02-12", [Line(a.Itub3, "Sell", 30, 2500)]));
        await client.DeleteAsync($"/trade-notes/{sell.Id}");
        await client.DeleteAsync($"/trade-notes/{buy.Id}");

        (await client.PostAsync($"/trade-notes/{sell.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync($"/trade-notes/{buy.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/trade-notes/{sell.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Positions(client)).Single().Quantity.ShouldBe(70);
    }

    [Fact]
    public async Task Editing_an_old_note_recalculates_what_comes_after()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var buy = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));
        await Create(client, Body("2025-02-12", [Line(a.Itub3, "Sell", 50, 2500)]));

        var edited = await client.PutAsJsonAsync($"/trade-notes/{buy.Id}",
            Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2200)], broker: "Íon"));

        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await edited.Content.ReadFromJsonAsync<NoteDto>())!.Broker.ShouldBe("Íon");
        (await Positions(client)).Single().RealizedCents.ShouldBe(15000); // 50 × (25,00 − 22,00)

        var tooFew = await client.PutAsJsonAsync($"/trade-notes/{buy.Id}", Body("2024-03-10", [Line(a.Itub3, "Buy", 40, 2200)]));
        tooFew.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Positions(client)).Single().Quantity.ShouldBe(50);
    }

    // O histórico tem um passo por ordem; notas do mesmo dia ficam na ordem em que foram lançadas.
    [Fact]
    public async Task History_lists_each_step_in_order()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var first = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)], feesCents: 1000));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var second = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Sell", 30, 2500)], broker: "Íon"));

        var steps = (await client.GetFromJsonAsync<List<StepDto>>($"/positions/{a.Itub3}"))!;

        steps.ShouldBe([
            new StepDto(first.Id, "2024-03-10", "XP", "Buy", 100, 200000, 1000, 100, 2000, 2010, null),
            new StepDto(second.Id, "2024-03-10", "Íon", "Sell", 30, 75000, 0, 70, 2000, 2010, 14700),
        ]);
    }

    // A nota lançada antes, levada para um dia que já tem outra, fica antes dela: a venda dela não pode usar a compra
    // lançada depois.
    [Fact]
    public async Task Note_moved_to_another_day_keeps_its_launch_order()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var early = await Create(client, Body("2024-03-20", [Line(a.Itub3, "Buy", 10, 2000)]));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await Create(client, Body("2024-03-15", [Line(a.Itub3, "Buy", 100, 2000)]));

        var moved = await client.PutAsJsonAsync($"/trade-notes/{early.Id}", Body("2024-03-15", [Line(a.Itub3, "Sell", 100, 2500)]));

        moved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(moved)).ShouldBe("Venda de 100 ITUB3 em 15/03/2024, mas a carteira tinha 0.");
    }

    [Fact]
    public async Task History_of_an_asset_without_notes_is_empty() =>
        await WithClient(async (client, a) =>
            (await client.GetFromJsonAsync<List<StepDto>>($"/positions/{a.Mxrf11}"))!.ShouldBeEmpty());

    [Fact]
    public async Task Note_puts_its_assets_in_the_portfolio()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;

        await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000), Line(a.Mxrf11, "Buy", 10, 1000)]));

        (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!.Select(h => h.Symbol).ShouldBe(["ITUB3", "MXRF11"]);
    }

    // Decisão do dono: tirar da carteira só com a posição zerada.
    [Fact]
    public async Task Holding_with_an_open_position_cannot_be_removed()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 80, 2000)]));
        var holding = (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!.Single();

        var refused = await client.DeleteAsync($"/holdings/{holding.Id}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Detail(refused)).ShouldBe("Ainda há 80 ITUB3 pelas notas. Só sai da carteira com a posição zerada.");
        await Create(client, Body("2024-03-11", [Line(a.Itub3, "Sell", 80, 2100)]));
        (await client.DeleteAsync($"/holdings/{holding.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Positions(client)).Single().RealizedCents.ShouldBe(8000);
    }

    // O tipo do ativo fica na linha: o fornecedor reclassificar o ativo não muda a corretagem da nota antiga.
    [Fact]
    public async Task Reclassified_asset_keeps_the_brokerage_split_of_old_notes()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var note = await Create(client, Body("2024-03-10",
            [Line(a.Mxrf11, "Buy", 100, 1000), Line(a.Itub3, "Buy", 10, 2000)], brokerageFeeCents: 490));
        note.Trades.Select(t => t.CostsCents).ShouldBe([0L, 490L]);

        _source.Assets = [.. _source.Assets.Where(x => x.Symbol != "MXRF11"), Listed("MXRF11", AssetKind.Stock, "MXRF11", "Maxi Renda")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);

        var again = (await client.GetFromJsonAsync<NoteDto>($"/trade-notes/{note.Id}"))!;
        again.Trades.Select(t => (t.AssetKind, t.CostsCents)).ShouldBe([("Fii", 0L), ("Stock", 490L)]);
    }

    [Fact]
    public async Task Notes_are_listed_newest_first()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var older = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));
        var newer = await Create(client, Body("2025-01-05", [Line(a.Bbse3, "Buy", 10, 3000)]));
        var sameDay = await Create(client, Body("2025-01-05", [Line(a.Bbse3, "Buy", 5, 3000)]));

        (await Notes(client)).Select(n => n.Id).ShouldBe([sameDay.Id, newer.Id, older.Id]);
        (await client.GetFromJsonAsync<List<NoteDto>>("/trade-notes?take=1"))!.Single().Id.ShouldBe(sameDay.Id);
    }

    [Theory]
    [InlineData("", "Buy", 100, 2000, "Escolha a corretora.")]
    [InlineData("XP", "Buy", 0, 2000, "A quantidade precisa ser maior que zero.")]
    [InlineData("XP", "Buy", 100, 0, "O preço precisa ser maior que zero.")]
    public async Task Invalid_notes_are_refused(string broker, string side, long quantity, long price, string message) =>
        await WithClient(async (client, a) =>
        {
            var response = await client.PostAsJsonAsync("/trade-notes", Body("2024-03-10", [Line(a.Itub3, side, quantity, price)], broker: broker));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).ShouldContain(message);
        });

    [Fact]
    public async Task Note_without_trades_is_refused() =>
        await WithClient(async (client, _) =>
            (await client.PostAsJsonAsync("/trade-notes", Body("2024-03-10", []))).StatusCode.ShouldBe(HttpStatusCode.BadRequest));

    [Fact]
    public async Task Brokerage_without_a_stock_is_refused() =>
        await WithClient(async (client, a) =>
        {
            var response = await client.PostAsJsonAsync("/trade-notes",
                Body("2024-03-10", [Line(a.Mxrf11, "Buy", 100, 1000)], brokerageFeeCents: 490));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Detail(response)).ShouldBe(
                "A corretagem desta nota não tem ação para receber. Marque que ela vale também para FII, ETF e outros.");
        });

    [Fact]
    public async Task Unknown_asset_is_not_found() =>
        await WithClient(async (client, _) =>
            (await client.PostAsJsonAsync("/trade-notes", Body("2024-03-10", [Line(Guid.CreateVersion7(), "Buy", 1, 100)])))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound));

    [Fact]
    public async Task Unknown_note_is_not_found() =>
        await WithClient(async (client, _) =>
        {
            (await client.GetAsync($"/trade-notes/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.DeleteAsync($"/trade-notes/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.PostAsync($"/trade-notes/{Guid.CreateVersion7()}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        });

    [Fact]
    public async Task Another_user_never_sees_or_touches_the_notes()
    {
        var (factory, client, a) = await Start();
        await using var _ = factory;
        var note = await Create(client, Body("2024-03-10", [Line(a.Itub3, "Buy", 100, 2000)]));
        var other = await factory.CreateAuthenticatedClientAsync();

        (await Notes(other)).ShouldBeEmpty();
        (await Positions(other)).ShouldBeEmpty();
        (await other.GetFromJsonAsync<List<StepDto>>($"/positions/{a.Itub3}"))!.ShouldBeEmpty();
        (await other.GetAsync($"/trade-notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/trade-notes/{note.Id}", Body("2024-03-10", [Line(a.Itub3, "Buy", 1, 2000)])))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/trade-notes/{note.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // A venda do outro usuário não enxerga a compra deste.
        (await other.PostAsJsonAsync("/trade-notes", Body("2024-03-11", [Line(a.Itub3, "Sell", 10, 2500)])))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_is_required() =>
        await WithClient(async (_, _) =>
        {
            await using var factory = new PrismaApiFactory(postgres.ConnectionString);
            var anonymous = factory.CreateHttpsClient();
            (await anonymous.GetAsync("/trade-notes")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync("/positions")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });

    private async Task WithClient(Func<HttpClient, Assets, Task> test)
    {
        var (factory, client, assets) = await Start();
        await using var _ = factory;
        await test(client, assets);
    }

    private async Task<(PrismaApiFactory, HttpClient, Assets)> Start()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets =
        [
            Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A."),
            Listed("BBSE3", AssetKind.Stock, "BB SEGURIDADE PARTICIPACOES S.A."),
            Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda"),
        ];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var client = await factory.CreateAuthenticatedClientAsync();
        return (factory, client, new Assets(await AssetId(client, "ITUB3"), await AssetId(client, "BBSE3"), await AssetId(client, "MXRF11")));
    }

    private static async Task<NoteDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/trade-notes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<NoteDto>())!;
    }

    private static async Task<List<NoteDto>> Notes(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NoteDto>>("/trade-notes"))!;

    private static async Task<List<PositionDto>> Positions(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PositionDto>>("/positions"))!;

    private static async Task<string?> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail;

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol).Id;
}
