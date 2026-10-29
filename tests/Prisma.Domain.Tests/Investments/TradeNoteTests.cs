using CsCheck;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7a Cálculo": a nota e o rateio dos custos entre as operações dela.
public sealed class TradeNoteTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2024, 3, 10);
    private static readonly Guid Itub3 = Guid.CreateVersion7();
    private static readonly Guid Bbse3 = Guid.CreateVersion7();
    private static readonly Guid Mxrf11 = Guid.CreateVersion7();
    private static readonly Guid Xina11 = Guid.CreateVersion7();
    private static readonly Guid Taee11 = Guid.CreateVersion7();

    private static Trade Buy(Guid asset, long quantity, long priceCents, AssetKind kind = AssetKind.Stock) =>
        new(asset, kind, TradeSide.Buy, quantity, priceCents);

    private static Trade Sell(Guid asset, long quantity, long priceCents, AssetKind kind = AssetKind.Stock) =>
        new(asset, kind, TradeSide.Sell, quantity, priceCents);

    private static Result<TradeNote> Create(Trade[] trades, long fees = 0, long brokerage = 0, AssetKind[]? kinds = null) =>
        TradeNote.Create(UserId, "XP", Day, trades, fees, brokerage, kinds ?? TradeNote.StockOnly);

    private static TradeNote Note(Trade[] trades, long fees = 0, long brokerage = 0, AssetKind[]? kinds = null) =>
        Create(trades, fees, brokerage, kinds).Value;

    // --- Validação ---

    [Fact]
    public void Note_needs_at_least_one_trade() =>
        Create([]).Error!.Message.ShouldBe("Inclua ao menos uma operação na nota.");

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(-1, 2000)]
    [InlineData(100, 0)]
    [InlineData(100, -5)]
    public void Quantity_and_price_must_be_positive(long quantity, long price) =>
        Create([Buy(Itub3, quantity, price)]).Error!.Type.ShouldBe(ErrorType.Validation);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Costs_cannot_be_negative(long fees, long brokerage) =>
        Create([Buy(Itub3, 100, 2000)], fees, brokerage).Error!.Type.ShouldBe(ErrorType.Validation);

    [Fact]
    public void Brokerage_without_a_marked_kind_to_receive_it_is_refused() =>
        Create([Buy(Mxrf11, 100, 1000, AssetKind.Fii)], brokerage: 490).Error!.Message
            .ShouldBe("Marque em quais ativos a corretagem foi cobrada.");

    [Fact]
    public void Brokerage_with_no_kind_marked_is_refused() =>
        Create([Buy(Itub3, 100, 2000)], brokerage: 490, kinds: []).Error!.Message
            .ShouldBe("Marque em quais ativos a corretagem foi cobrada.");

    [Fact]
    public void Fund_only_note_is_fine_without_brokerage_or_with_its_kind_marked()
    {
        Create([Buy(Mxrf11, 100, 1000, AssetKind.Fii)], fees: 50).IsSuccess.ShouldBeTrue();
        Create([Buy(Mxrf11, 100, 1000, AssetKind.Fii)], fees: 50, kinds: []).IsSuccess.ShouldBeTrue();
        Create([Buy(Mxrf11, 100, 1000, AssetKind.Fii)], 50, 490, [AssetKind.Fii]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Costs_above_the_trade_value_are_accepted() =>
        Create([Buy(Itub3, 1, 100)], fees: 500).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Gross_value_is_quantity_times_price() =>
        Buy(Itub3, 50, 2010).GrossCents.ShouldBe(100500);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Note_needs_a_broker(string broker) =>
        TradeNote.Create(UserId, broker, Day, [Buy(Itub3, 100, 2000)], 0, 0, TradeNote.StockOnly).Error!.Message.ShouldBe("Escolha a corretora.");

    [Fact]
    public void Broker_is_trimmed_and_limited()
    {
        TradeNote.Create(UserId, "  BTG Pactual ", Day, [Buy(Itub3, 100, 2000)], 0, 0, TradeNote.StockOnly).Value.Broker.ShouldBe("BTG Pactual");
        TradeNote.Create(UserId, new string('x', 61), Day, [Buy(Itub3, 100, 2000)], 0, 0, TradeNote.StockOnly).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Lines_are_numbered_in_the_typed_order() =>
        Note([Buy(Bbse3, 10, 3000), Buy(Itub3, 100, 2000)]).Trades.Select(t => (t.AssetId, t.LineNumber))
            .ShouldBe([(Bbse3, 1), (Itub3, 2)]);

    [Fact]
    public void Update_replaces_everything()
    {
        var note = Note([Buy(Itub3, 100, 2000)], fees: 50);

        note.Update("Íon", Day.AddDays(1), [Sell(Bbse3, 5, 3000), Buy(Itub3, 1, 2000)], 10, 490, [AssetKind.Etf, AssetKind.Stock]).ShouldBeNull();

        (note.Broker, note.Date, note.FeesCents, note.BrokerageFeeCents).ShouldBe(("Íon", Day.AddDays(1), 10L, 490L));
        note.BrokerageFeeKinds.ShouldBe([AssetKind.Stock, AssetKind.Etf]);
        note.Trades.Select(t => (t.AssetId, t.Side, t.LineNumber)).ShouldBe([(Bbse3, TradeSide.Sell, 1), (Itub3, TradeSide.Buy, 2)]);
    }

    [Fact]
    public void Refused_update_keeps_the_note()
    {
        var note = Note([Buy(Itub3, 100, 2000)], fees: 50);

        note.Update("Íon", Day, [], 0, 0, TradeNote.StockOnly).ShouldNotBeNull();

        (note.Broker, note.FeesCents, note.Trades.Count).ShouldBe(("XP", 50L, 1));
    }

    // --- Taxas: proporcionais ao valor, entre compras e vendas ---

    [Fact]
    public void Fees_split_by_trade_value() =>
        Note([Buy(Itub3, 400, 2000), Buy(Bbse3, 100, 2000)], fees: 1000).AllocateCosts().ShouldBe([800, 200]);

    [Fact]
    public void Fees_leftover_cent_goes_to_the_largest_remainder() =>
        Note([Buy(Itub3, 1, 10000), Buy(Bbse3, 1, 20000), Buy(Mxrf11, 1, 70000, AssetKind.Fii)], fees: 11)
            .AllocateCosts().ShouldBe([1, 2, 8]);

    [Fact]
    public void Fees_tie_goes_to_the_earlier_trade() =>
        Note([Buy(Itub3, 1, 100000), Buy(Bbse3, 1, 100000), Buy(Mxrf11, 1, 100000, AssetKind.Fii)], fees: 100)
            .AllocateCosts().ShouldBe([34, 33, 33]);

    [Fact]
    public void Fees_split_between_buys_and_sells() =>
        Note([Buy(Itub3, 100, 2000), Sell(Bbse3, 100, 3000)], fees: 100).AllocateCosts().ShouldBe([40, 60]);

    [Fact]
    public void Note_without_costs_allocates_zero() =>
        Note([Buy(Itub3, 100, 2000), Buy(Bbse3, 10, 3000)]).AllocateCosts().ShouldBe([0, 0]);

    // --- Corretagem: igual entre as ordens, proporcional dentro da ordem ---

    [Fact]
    public void Brokerage_splits_by_order_not_by_line() =>
        Note([Buy(Itub3, 50, 2000), Buy(Itub3, 30, 2010), Buy(Bbse3, 20, 3200)], brokerage: 1000)
            .AllocateCosts().ShouldBe([312, 188, 500]);

    [Fact]
    public void Brokerage_goes_only_to_stocks_by_default() =>
        Note([Buy(Mxrf11, 100, 1000, AssetKind.Fii), Buy(Itub3, 10, 2000)], brokerage: 490)
            .AllocateCosts().ShouldBe([0, 490]);

    // Caso do dono (2026-10-06): venda de ação e compra de FII na mesma nota. A corretagem fica toda na venda da ação
    // (reduz o que ela rende); o FII recebe só a parte dele nas taxas.
    [Fact]
    public void Stock_sale_and_fund_buy_put_the_brokerage_on_the_sale() =>
        Note([Sell(Itub3, 100, 1000), Buy(Mxrf11, 100, 1000, AssetKind.Fii)], fees: 60, brokerage: 490)
            .AllocateCosts().ShouldBe([30 + 490, 30]);

    [Fact]
    public void Brokerage_goes_to_every_kind_when_all_are_marked() =>
        Note([Buy(Mxrf11, 100, 1000, AssetKind.Fii), Buy(Itub3, 10, 2000)], brokerage: 490, kinds: [.. TradeNote.AllKinds])
            .AllocateCosts().ShouldBe([245, 245]);

    // Caso do dono (2026-10-08): venda de XINA11 (ETF) e compra de MXRF11 (FII), corretagem só no ETF. O FII fica só
    // com a parte dele nas taxas.
    [Fact]
    public void Etf_sale_and_fund_buy_put_the_brokerage_on_the_etf_only() =>
        Note([Sell(Xina11, 50, 1000, AssetKind.Etf), Buy(Mxrf11, 50, 1000, AssetKind.Fii)], fees: 30, brokerage: 490, kinds: [AssetKind.Etf])
            .AllocateCosts().ShouldBe([15 + 490, 15]);

    [Fact]
    public void Brokerage_can_skip_the_stocks() =>
        Note([Buy(Itub3, 10, 2000), Sell(Xina11, 50, 1000, AssetKind.Etf)], brokerage: 490, kinds: [AssetKind.Etf])
            .AllocateCosts().ShouldBe([0, 490]);

    [Fact]
    public void Two_marked_kinds_split_the_brokerage_by_order() =>
        Note([Buy(Itub3, 10, 2000), Sell(Xina11, 50, 1000, AssetKind.Etf), Buy(Mxrf11, 50, 1000, AssetKind.Fii)], brokerage: 491,
                kinds: [AssetKind.Etf, AssetKind.Stock])
            .AllocateCosts().ShouldBe([246, 245, 0]);

    // Marcar um tipo que não está na nota não muda nada: só as ordens da nota recebem.
    [Fact]
    public void A_marked_kind_absent_from_the_note_receives_nothing() =>
        Note([Buy(Itub3, 10, 2000), Buy(Mxrf11, 50, 1000, AssetKind.Fii)], brokerage: 490, kinds: [AssetKind.Stock, AssetKind.Etf])
            .AllocateCosts().ShouldBe([490, 0]);

    // Unit é ação para a corretagem: "Unit" vira "Stock", sem repetição, em ordem fixa.
    [Fact]
    public void Kinds_are_kept_without_unit_or_repetition() =>
        Note([Buy(Itub3, 10, 2000)], brokerage: 490, kinds: [AssetKind.Fii, AssetKind.Unit, AssetKind.Stock, AssetKind.Fii])
            .BrokerageFeeKinds.ShouldBe([AssetKind.Stock, AssetKind.Fii]);

    [Fact]
    public void Marking_units_also_marks_stocks() =>
        Note([Buy(Itub3, 10, 2000), Buy(Taee11, 10, 3000, AssetKind.Unit)], brokerage: 490, kinds: [AssetKind.Unit])
            .AllocateCosts().ShouldBe([245, 245]);

    // A marcação antiga (I.7b1): ligada, todos os tipos; desligada ou ausente, só ação. A lista nova, quando vem, decide.
    [Fact]
    public void Old_mark_translates_to_kinds()
    {
        TradeNote.BrokerageKindsFrom(null, true).ShouldBe(TradeNote.AllKinds);
        TradeNote.BrokerageKindsFrom(null, false).ShouldBe(TradeNote.StockOnly);
        TradeNote.BrokerageKindsFrom(null, null).ShouldBe(TradeNote.StockOnly);
        TradeNote.BrokerageKindsFrom([AssetKind.Etf], true).ShouldBe([AssetKind.Etf]);
        TradeNote.AllKinds.ShouldNotContain(AssetKind.Unit);
        TradeNote.AllKinds.Count.ShouldBe(Enum.GetValues<AssetKind>().Length - 1);
    }

    [Theory]
    [InlineData(AssetKind.Stock, 490)]
    [InlineData(AssetKind.Unit, 490)]
    [InlineData(AssetKind.Fii, 0)]
    [InlineData(AssetKind.Etf, 0)]
    [InlineData(AssetKind.Bdr, 0)]
    [InlineData(AssetKind.Unknown, 0)]
    public void Units_count_as_stocks_for_brokerage(AssetKind kind, long expected) =>
        Note([Buy(Mxrf11, 10, 1000, kind), Buy(Itub3, 10, 2000)], brokerage: 980)
            .AllocateCosts()[0].ShouldBe(expected);

    // Cada tipo marcado sozinho: só ele recebe (ação e unit juntas).
    [Theory]
    [InlineData(AssetKind.Fii)]
    [InlineData(AssetKind.Etf)]
    [InlineData(AssetKind.Bdr)]
    [InlineData(AssetKind.FiInfra)]
    [InlineData(AssetKind.Unknown)]
    public void Each_marked_kind_receives_alone(AssetKind kind) =>
        Note([Buy(Mxrf11, 10, 1000, kind), Buy(Itub3, 10, 2000)], brokerage: 980, kinds: [kind])
            .AllocateCosts().ShouldBe([980, 0]);

    [Fact]
    public void Brokerage_extra_cent_goes_to_the_first_order() =>
        Note([Buy(Itub3, 1, 1000), Buy(Bbse3, 1, 1000), Buy(Guid.CreateVersion7(), 1, 1000)], brokerage: 1000)
            .AllocateCosts().ShouldBe([334, 333, 333]);

    [Fact]
    public void Buy_and_sell_of_the_same_asset_are_two_orders() =>
        Note([Buy(Itub3, 100, 2000), Sell(Itub3, 100, 2100)], brokerage: 1000).AllocateCosts().ShouldBe([500, 500]);

    [Fact]
    public void Fees_and_brokerage_add_up_per_trade() =>
        Note([Buy(Itub3, 400, 2000), Buy(Bbse3, 100, 2000)], fees: 1000, brokerage: 990)
            .AllocateCosts().ShouldBe([800 + 495, 200 + 495]);

    // A soma do rateio é sempre taxas + corretagem, para qualquer nota válida.
    [Fact]
    public void Allocated_costs_always_sum_to_the_note_costs()
    {
        var kinds = Gen.OneOfConst(AssetKind.Stock, AssetKind.Unit, AssetKind.Fii, AssetKind.Etf);
        var assets = Gen.OneOfConst(Itub3, Bbse3, Mxrf11);
        var trade = Gen.Select(assets, Gen.Bool, Gen.Long[1, 100_000], Gen.Long[1, 1_000_000]);

        Gen.Select(trade.Array[1, 12], kinds.Array[3], Gen.Long[0, 10_000_000], Gen.Long[0, 1_000_000], kinds.Array[0, 4])
            .Sample((lines, kindOf, fees, brokerage, marked) =>
            {
                // O mesmo ativo tem um tipo só.
                Guid[] ids = [Itub3, Bbse3, Mxrf11];
                var trades = lines.Select(l => new Trade(l.Item1, kindOf[Array.IndexOf(ids, l.Item1)],
                    l.Item2 ? TradeSide.Buy : TradeSide.Sell, l.Item3, l.Item4)).ToArray();
                var note = Create(trades, fees, brokerage, marked);
                return !note.IsSuccess || note.Value.AllocateCosts().Sum() == fees + brokerage;
            });
    }

    // --- Caixa da corretora (docs/investimentos.md, "I.8"): o líquido que a corretora liquida ---

    // Positivo: o dinheiro sai da conta (compra). Negativo: entra (venda).
    [Fact]
    public void Net_cash_of_a_buy_is_the_gross_plus_the_costs()
    {
        // O caso do dono: 100 PETR4 × R$ 57,00 e R$ 49,70 de custos.
        var note = Note([Buy(Itub3, 100, 5700)], fees: 4970);

        note.NetCashCents().ShouldBe(574970);
    }

    [Fact]
    public void Net_cash_of_a_sell_is_negative_and_the_costs_reduce_what_comes_in()
    {
        var note = Note([Sell(Itub3, 100, 6000)], fees: 5000);

        note.NetCashCents().ShouldBe(-595000);
    }

    [Fact]
    public void Net_cash_of_a_note_with_buy_and_sell_is_what_the_broker_settles_in_the_end()
    {
        // Compra R$ 1.000,00 e venda R$ 1.500,00, custos R$ 10,00 divididos pelo valor (4,00 e 6,00):
        // (1.000,00 + 4,00) − (1.500,00 − 6,00) = −490,00.
        var note = Note([Buy(Itub3, 10, 10000), Sell(Bbse3, 10, 15000)], fees: 1000);

        note.NetCashCents().ShouldBe(-49000);
    }

    [Fact]
    public void Net_cash_is_zero_when_buy_and_sell_cancel_out()
    {
        var note = Note([Buy(Itub3, 10, 10000), Sell(Bbse3, 10, 10000)]);

        note.NetCashCents().ShouldBe(0);
    }

    [Fact]
    public void Brokerage_fee_is_a_cost_like_the_others()
    {
        var note = Note([Buy(Itub3, 100, 2000)], fees: 300, brokerage: 500);

        note.NetCashCents().ShouldBe(200800);
    }

    // Os custos entram na compra e saem da venda: no líquido, compras − vendas + todos os custos, para qualquer nota.
    [Fact]
    public void Net_cash_is_buys_minus_sells_plus_all_the_costs()
    {
        var trade = Gen.Select(Gen.Int[1, 500], Gen.Int[1, 50000], Gen.Bool, (quantity, price, isBuy) =>
            isBuy ? Buy(Guid.CreateVersion7(), quantity, price) : Sell(Guid.CreateVersion7(), quantity, price));

        Gen.Select(trade.List[1, 6], Gen.Int[0, 100000], Gen.Int[0, 100000], (trades, fees, brokerage) => (trades, fees, brokerage))
            .Sample(input =>
            {
                var note = Note([.. input.trades], input.fees, input.brokerage);
                var buys = input.trades.Where(t => t.Side == TradeSide.Buy).Sum(t => t.GrossCents);
                var sells = input.trades.Where(t => t.Side == TradeSide.Sell).Sum(t => t.GrossCents);

                // A corretagem só vai para quem a corretora cobra (ação); sem ação, é recusada na criação.
                var hasStock = input.trades.Any(t => t.BrokerageKind == AssetKind.Stock);
                var costs = input.fees + (hasStock ? input.brokerage : 0);
                if (!hasStock && input.brokerage > 0)
                    return true;

                return note.NetCashCents() == buys - sells + costs;
            });
    }
}
