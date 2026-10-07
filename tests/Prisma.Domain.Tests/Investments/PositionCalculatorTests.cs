using CsCheck;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7a Cálculo": posição, os dois preços médios e o resultado da venda, a partir das notas.
public sealed class PositionCalculatorTests
{
    private static readonly Guid Itub3 = Guid.CreateVersion7();
    private static readonly Guid Bbse3 = Guid.CreateVersion7();
    private static readonly DateOnly Day1 = new(2024, 3, 10);

    private static TradeNote Note(DateOnly date, Trade[] trades, long fees = 0, long brokerage = 0) =>
        TradeNote.Create(Guid.CreateVersion7(), "XP", date, trades, fees, brokerage, false).Value;

    private static TradeNote Buy(DateOnly date, long quantity, long price, long fees = 0, Guid? asset = null) =>
        Note(date, [Trade(asset ?? Itub3, TradeSide.Buy, quantity, price)], fees);

    private static TradeNote Sell(DateOnly date, long quantity, long price, long fees = 0, Guid? asset = null) =>
        Note(date, [Trade(asset ?? Itub3, TradeSide.Sell, quantity, price)], fees);

    private static Trade Trade(Guid asset, TradeSide side, long quantity, long price) =>
        new(asset, AssetKind.Stock, side, quantity, price);

    private static string Symbol(Guid asset) => asset == Itub3 ? "ITUB3" : "BBSE3";

    private static Result<IReadOnlyList<AssetPosition>> Build(params TradeNote[] notes) => PositionCalculator.Build(notes, Symbol);

    private static AssetPosition Only(params TradeNote[] notes) =>
        Build(notes).Value.ShouldHaveSingleItem();

    private static DateOnly Day(int n) => Day1.AddDays(n);

    [Fact]
    public void Buy_costs_go_into_the_acquisition_cost()
    {
        var position = Only(Buy(Day1, 100, 2000, fees: 1000));

        position.Current.ShouldBe(new PositionTotals(100, 200000, 201000));
        position.Current.AveragePriceCents.ShouldBe(2000);
        position.Current.AverageCostCents.ShouldBe(2010);
    }

    [Fact]
    public void Two_lines_of_one_order_make_one_step()
    {
        var note = Note(Day1,
            [Trade(Itub3, TradeSide.Buy, 50, 2000), Trade(Itub3, TradeSide.Buy, 30, 2010), Trade(Bbse3, TradeSide.Buy, 20, 3200)],
            brokerage: 1000);

        var itub3 = Build(note).Value.Single(p => p.AssetId == Itub3);

        itub3.Current.ShouldBe(new PositionTotals(80, 160300, 160800));
        itub3.Current.AveragePriceCents.ShouldBe(2004); // 20,0375
        itub3.Current.AverageCostCents.ShouldBe(2010);
        var step = itub3.History.ShouldHaveSingleItem();
        (step.Side, step.Quantity, step.GrossCents, step.CostsCents).ShouldBe((TradeSide.Buy, 80L, 160300L, 500L));
        step.Note.ShouldBe(note);
    }

    [Fact]
    public void Full_sale_takes_sale_costs_from_the_proceeds()
    {
        var position = Only(Buy(Day1, 100, 2000), Sell(Day(1), 100, 2500, fees: 1000));

        position.History[1].RealizedCents.ShouldBe(49000);
        position.RealizedCents.ShouldBe(49000);
        position.Current.ShouldBe(new PositionTotals(0, 0, 0));
        position.Current.AveragePriceCents.ShouldBeNull();
        position.Current.AverageCostCents.ShouldBeNull();
    }

    [Fact]
    public void Partial_sale_keeps_the_average_prices_of_what_stays()
    {
        var position = Only(Buy(Day1, 100, 2000, fees: 1000), Sell(Day(1), 30, 2500));

        position.RealizedCents.ShouldBe(14700); // 750,00 − 603,00
        position.Current.ShouldBe(new PositionTotals(70, 140000, 140700));
        position.Current.AveragePriceCents.ShouldBe(2000);
        position.Current.AverageCostCents.ShouldBe(2010);
    }

    [Fact]
    public void Rounding_on_sales_never_loses_a_cent()
    {
        var position = Only(Buy(Day1, 3, 1000, fees: 1), Sell(Day(1), 1, 1000), Sell(Day(2), 1, 1000), Sell(Day(3), 1, 1000));

        // Saem 10,00, 10,01 e 10,00 do custo de aquisição: resultados 0, −0,01 e 0.
        position.History.Skip(1).Select(s => s.RealizedCents).ShouldBe([0L, -1L, 0L]);
        position.Current.ShouldBe(new PositionTotals(0, 0, 0));
    }

    [Fact]
    public void Two_buys_average_the_price() =>
        Only(Buy(Day1, 100, 2000), Buy(Day(1), 100, 2200)).Current.AveragePriceCents.ShouldBe(2100);

    [Fact]
    public void Costs_above_the_trade_value_give_a_loss()
    {
        var position = Only(Buy(Day1, 1, 100, fees: 500), Sell(Day(1), 1, 100, fees: 500));

        position.History[0].After.AverageCostCents.ShouldBe(600);
        position.RealizedCents.ShouldBe(-1000);
    }

    [Fact]
    public void Buying_again_after_selling_everything_starts_from_zero() =>
        Only(Buy(Day1, 10, 2000), Sell(Day(1), 10, 2500), Buy(Day(2), 10, 3000)).Current.AveragePriceCents.ShouldBe(3000);

    [Fact]
    public void Selling_more_than_held_is_refused()
    {
        var error = Build(Buy(Day1, 100, 2000), Sell(Day(1), 110, 2500)).Error!;

        error.Type.ShouldBe(ErrorType.Validation);
        error.Message.ShouldBe("Venda de 110 ITUB3 em 11/03/2024, mas a carteira tinha 100.");
    }

    [Fact]
    public void Selling_with_nothing_held_is_refused() =>
        Build(Sell(Day1, 10, 2500)).Error!.Message
            .ShouldBe("Venda de 10 ITUB3 em 10/03/2024, mas a carteira tinha 0.");

    [Fact]
    public void Notes_are_processed_in_date_order() =>
        Build(Sell(Day(1), 100, 2500), Buy(Day1, 100, 2000)).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Same_day_notes_keep_the_given_order()
    {
        Build(Buy(Day1, 100, 2000), Sell(Day1, 100, 2500)).IsSuccess.ShouldBeTrue();
        Build(Sell(Day1, 100, 2500), Buy(Day1, 100, 2000)).IsSuccess.ShouldBeFalse();
    }

    // Day trade ignorado: compra e venda na mesma nota são ordens comuns, na ordem digitada.
    [Fact]
    public void Buy_and_sell_in_one_note_follow_the_typed_order()
    {
        var note = Note(Day1, [Trade(Itub3, TradeSide.Buy, 100, 2000), Trade(Itub3, TradeSide.Sell, 100, 2100)], fees: 100);

        var position = Only(note);

        position.History.Select(s => s.Side).ShouldBe([TradeSide.Buy, TradeSide.Sell]);
        position.RealizedCents.ShouldBe(210000 - 51 - (200000 + 49)); // taxas 0,49 na compra e 0,51 na venda
    }

    [Fact]
    public void Each_asset_has_its_own_position()
    {
        var positions = Build(Buy(Day1, 100, 2000), Buy(Day1, 10, 3000, asset: Bbse3)).Value;

        positions.Select(p => (p.AssetId, p.Current.Quantity)).ShouldBe([(Itub3, 100L), (Bbse3, 10L)]);
    }

    [Fact]
    public void History_shows_the_position_after_each_step()
    {
        var position = Only(Buy(Day1, 100, 2000), Buy(Day(1), 100, 2200), Sell(Day(2), 50, 2500));

        position.History.Select(s => (s.After.Quantity, s.After.AveragePriceCents, s.RealizedCents))
            .ShouldBe([(100L, (long?)2000, (long?)null), (200L, 2100, null), (150L, 2100, 20000L)]);
    }

    [Fact]
    public void No_notes_no_positions() =>
        Build().Value.ShouldBeEmpty();

    // Comprar e vender tudo em partes, em qualquer ordem: os custos zeram e a soma dos resultados é o líquido das
    // vendas menos o custo de aquisição de tudo.
    [Fact]
    public void Selling_everything_releases_exactly_the_acquisition_cost()
    {
        var chunk = Gen.Select(Gen.Long[1, 10_000], Gen.Long[1, 500_000], Gen.Long[0, 50_000], Gen.Long[1, 500_000], Gen.Long[0, 50_000]);

        Gen.Select(chunk.Array[1, 15], Gen.Int).Sample((chunks, seed) =>
        {
            var buys = chunks.Select((c, i) => Buy(Day(i), c.Item1, c.Item2, c.Item3)).ToList();
            var random = new Random(seed);
            var sells = chunks.OrderBy(_ => random.Next())
                .Select((c, i) => Sell(Day(100 + i), c.Item1, c.Item4, c.Item5)).ToList();

            var position = Only([.. buys, .. sells]);

            var acquisition = chunks.Sum(c => c.Item1 * c.Item2 + c.Item3);
            var net = chunks.Sum(c => c.Item1 * c.Item4 - c.Item5);
            return position.Current == new PositionTotals(0, 0, 0) && position.RealizedCents == net - acquisition;
        });
    }
}
