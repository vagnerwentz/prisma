using CsCheck;
using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7e": a carteira em um ano (no fim de 31/12, ou hoje no ano corrente), pelo custo, com os
// grupos, os pesos que fecham 100%, e os proventos e o resultado das vendas do ano.
public sealed class PortfolioSnapshotTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Bcff11 = Guid.CreateVersion7();
    private static readonly Guid Bthf11 = Guid.CreateVersion7();
    private static readonly Guid Itub3 = Guid.CreateVersion7();
    private static readonly Guid Taee11 = Guid.CreateVersion7();
    private static readonly Guid Mxrf11 = Guid.CreateVersion7();
    private static readonly Guid Xina11 = Guid.CreateVersion7();
    private static readonly Guid Knca11 = Guid.CreateVersion7();
    private static readonly Guid Bbas3 = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, (string Symbol, AssetKind Kind)> Catalog = new()
    {
        [Bcff11] = ("BCFF11", AssetKind.Fii),
        [Bthf11] = ("BTHF11", AssetKind.Fii),
        [Itub3] = ("ITUB3", AssetKind.Stock),
        [Taee11] = ("TAEE11", AssetKind.Unit),
        [Mxrf11] = ("MXRF11", AssetKind.Fii),
        [Xina11] = ("XINA11", AssetKind.Etf),
        [Knca11] = ("KNCA11", AssetKind.FiAgro),
        [Bbas3] = ("BBAS3", AssetKind.Stock),
    };

    private static TradeNote Note(string date, Guid asset, TradeSide side, long quantity, long price) =>
        TradeNote.Create(Guid.CreateVersion7(), "Íon", DateOnly.Parse(date), [new Trade(asset, Catalog[asset].Kind, side, quantity, price)],
            0, 0, TradeNote.StockOnly).Value;

    private static TradeNote Buy(string date, Guid asset, long quantity, long price) => Note(date, asset, TradeSide.Buy, quantity, price);

    private static TradeNote Sell(string date, Guid asset, long quantity, long price) => Note(date, asset, TradeSide.Sell, quantity, price);

    private static PortfolioPayout Payout(string date, Guid asset, long cents) => new(asset, DateOnly.Parse(date), cents);

    private static Result<PortfolioSnapshot> Snapshot(
        int year, TradeNote[] notes, PortfolioPayout[]? payouts = null, CorporateEvent[]? events = null)
    {
        var positions = PositionCalculator.Build(notes, events ?? [], id => Catalog[id].Symbol).Value;
        return PortfolioSnapshot.Build(year, Today, positions, payouts ?? [], id => Catalog[id].Kind, id => Catalog[id].Symbol);
    }

    private static PortfolioAsset Asset(PortfolioSnapshot snapshot, Guid id) => snapshot.Assets.Single(a => a.AssetId == id);

    [Theory]
    [InlineData(AssetKind.Stock, PortfolioGroup.Stocks)]
    [InlineData(AssetKind.Unit, PortfolioGroup.Stocks)]
    [InlineData(AssetKind.Fii, PortfolioGroup.Fiis)]
    [InlineData(AssetKind.Etf, PortfolioGroup.Etfs)]
    [InlineData(AssetKind.Bdr, PortfolioGroup.Bdrs)]
    [InlineData(AssetKind.FiInfra, PortfolioGroup.OtherFunds)]
    [InlineData(AssetKind.FiAgro, PortfolioGroup.OtherFunds)]
    [InlineData(AssetKind.Fip, PortfolioGroup.OtherFunds)]
    [InlineData(AssetKind.Fidc, PortfolioGroup.OtherFunds)]
    [InlineData(AssetKind.OtherFund, PortfolioGroup.OtherFunds)]
    [InlineData(AssetKind.Unknown, PortfolioGroup.Other)]
    public void Each_kind_has_its_group(AssetKind kind, PortfolioGroup group) => PortfolioGroups.Of(kind).ShouldBe(group);

    [Fact]
    public void Every_kind_is_mapped() =>
        Enum.GetValues<AssetKind>().Select(PortfolioGroups.Of).ShouldAllBe(g => Enum.IsDefined(g));

    // O caso do dono: BCFF11 comprado em 2020 e 2023, desdobrado 1:8 em 28/11/2023 e convertido em BTHF11 (100:80) em
    // 16/12/2024. A carteira de cada ano mostra o que ele tinha no fim daquele ano.
    [Fact]
    public void Owner_case_year_by_year()
    {
        TradeNote[] notes = [Buy("2020-09-30", Bcff11, 10, 9028), Buy("2023-10-02", Bcff11, 100, 7219)];
        CorporateEvent[] events =
        [
            CorporateEvent.Create("split", CorporateEventKind.Split, Bcff11, null, new DateOnly(2023, 11, 28), 1, 8, "teste", Now).Value,
            CorporateEvent.Create("conv", CorporateEventKind.Conversion, Bcff11, Bthf11, new DateOnly(2024, 12, 16), 100, 80, "teste", Now).Value,
        ];

        var y2019 = Snapshot(2019, notes, events: events).Value;
        var y2020 = Snapshot(2020, notes, events: events).Value;
        var y2023 = Snapshot(2023, notes, events: events).Value;
        var y2024 = Snapshot(2024, notes, events: events).Value;

        (y2019.InvestedCents, y2019.Assets.Count, y2019.Groups.Count).ShouldBe((0L, 0, 0));
        y2020.Assets.Select(a => (a.AssetId, a.Totals)).ShouldBe([(Bcff11, new PositionTotals(10, 90280, 90280))]);
        y2023.Assets.Select(a => (a.AssetId, a.Totals)).ShouldBe([(Bcff11, new PositionTotals(880, 812180, 812180))]);
        y2024.Assets.Select(a => (a.AssetId, a.Totals)).ShouldBe([(Bthf11, new PositionTotals(704, 812180, 812180))]);
        y2024.InvestedCents.ShouldBe(812180);
        y2024.Groups.Single().ShouldBe(new PortfolioSlice(PortfolioGroup.Fiis, 812180, 10000, 1, 0, 0));
    }

    [Fact]
    public void Past_year_is_the_end_of_december_31_and_the_current_year_is_today()
    {
        TradeNote[] notes = [Buy("2024-12-31", Itub3, 10, 2000), Buy("2025-01-01", Itub3, 5, 2000)];

        var y2024 = Snapshot(2024, notes).Value;
        var y2026 = Snapshot(2026, notes).Value;

        (y2024.AsOf, y2024.Year, y2024.InvestedCents).ShouldBe((new DateOnly(2024, 12, 31), 2024, 20000L));
        (y2026.AsOf, y2026.InvestedCents).ShouldBe((Today, 30000L));
    }

    [Fact]
    public void Note_dated_after_today_does_not_count_yet()
    {
        var snapshot = Snapshot(2026, [Buy("2026-03-02", Itub3, 10, 2000), Buy("2026-11-02", Itub3, 10, 2000)]).Value;

        Asset(snapshot, Itub3).Totals.Quantity.ShouldBe(10);
        snapshot.InvestedCents.ShouldBe(20000);
    }

    // Investido é o custo de aquisição (com os custos) só do que ainda se tem.
    [Fact]
    public void Invested_is_the_cost_basis_of_what_is_still_held()
    {
        var withCosts = TradeNote.Create(Guid.CreateVersion7(), "XP", new DateOnly(2025, 3, 10),
            [new Trade(Itub3, AssetKind.Stock, TradeSide.Buy, 100, 2000)], 150, 0, TradeNote.StockOnly).Value;

        var snapshot = Snapshot(2025, [withCosts, Buy("2025-03-11", Mxrf11, 10, 1000), Sell("2025-06-01", Mxrf11, 10, 1100)]).Value;

        snapshot.InvestedCents.ShouldBe(200150);
        Asset(snapshot, Itub3).Totals.ShouldBe(new PositionTotals(100, 200000, 200150));
    }

    // Unit fica com as ações; FI-Agro em "Outros fundos". Do maior grupo para o menor.
    [Fact]
    public void Groups_add_their_assets_and_weigh_against_the_total()
    {
        var snapshot = Snapshot(2026,
        [
            Buy("2026-01-05", Itub3, 10, 500), Buy("2026-01-05", Taee11, 10, 100), Buy("2026-01-05", Mxrf11, 10, 300),
            Buy("2026-01-05", Knca11, 10, 100),
        ]).Value;

        snapshot.InvestedCents.ShouldBe(10000);
        snapshot.Groups.ShouldBe(
        [
            new PortfolioSlice(PortfolioGroup.Stocks, 6000, 6000, 2, 0, 0),
            new PortfolioSlice(PortfolioGroup.Fiis, 3000, 3000, 1, 0, 0),
            new PortfolioSlice(PortfolioGroup.OtherFunds, 1000, 1000, 1, 0, 0),
        ]);
        snapshot.Assets.Select(a => (a.AssetId, a.Group, a.WeightBasisPoints)).ShouldBe(
        [
            (Itub3, PortfolioGroup.Stocks, 5000), (Mxrf11, PortfolioGroup.Fiis, 3000), (Knca11, PortfolioGroup.OtherFunds, 1000),
            (Taee11, PortfolioGroup.Stocks, 1000),
        ]);
    }

    // Empate no custo: pelo código. Empate de grupo: na ordem dos grupos.
    [Fact]
    public void Ties_follow_the_symbol_and_the_group_order()
    {
        var snapshot = Snapshot(2026, [Buy("2026-01-05", Xina11, 1, 1000), Buy("2026-01-05", Mxrf11, 1, 1000), Buy("2026-01-05", Itub3, 1, 1000)]).Value;

        snapshot.Assets.Select(a => a.AssetId).ShouldBe([Itub3, Mxrf11, Xina11]);
        snapshot.Groups.Select(g => g.Group).ShouldBe([PortfolioGroup.Stocks, PortfolioGroup.Fiis, PortfolioGroup.Etfs]);
        // 1/3 cada: a sobra vai para o primeiro.
        snapshot.Groups.Select(g => g.WeightBasisPoints).ShouldBe([3334, 3333, 3333]);
    }

    // Proventos do ano até a data: no ano corrente, o futuro é "a receber" e fica de fora.
    [Fact]
    public void Payouts_of_the_year_up_to_the_date()
    {
        TradeNote[] notes = [Buy("2024-01-10", Mxrf11, 100, 1000)];
        PortfolioPayout[] payouts =
        [
            Payout("2024-12-31", Mxrf11, 100), Payout("2025-01-15", Mxrf11, 110), Payout("2025-12-31", Mxrf11, 120),
            Payout("2026-10-09", Mxrf11, 130), Payout("2026-10-15", Mxrf11, 140),
        ];

        Snapshot(2025, notes, payouts).Value.PayoutsCents.ShouldBe(230);
        var y2026 = Snapshot(2026, notes, payouts).Value;
        y2026.PayoutsCents.ShouldBe(130);
        Asset(y2026, Mxrf11).PayoutsCents.ShouldBe(130);
        y2026.Groups.Single().PayoutsCents.ShouldBe(130);
    }

    // O resultado fica no ano da venda, também para o ativo vendido inteiro, que entra com peso zero.
    [Fact]
    public void Realized_of_the_year_and_the_asset_sold_in_it()
    {
        TradeNote[] notes =
        [
            Buy("2024-02-01", Itub3, 100, 2000), Buy("2024-02-01", Mxrf11, 10, 1000),
            Sell("2025-05-02", Itub3, 100, 2500), Sell("2026-05-04", Mxrf11, 5, 900),
        ];

        var y2025 = Snapshot(2025, notes).Value;
        var y2026 = Snapshot(2026, notes).Value;

        y2025.RealizedCents.ShouldBe(50000);
        var sold = Asset(y2025, Itub3);
        (sold.Totals, sold.WeightBasisPoints, sold.RealizedCents).ShouldBe((PositionTotals.Empty, 0, 50000L));
        y2025.Groups.Single(g => g.Group == PortfolioGroup.Stocks).ShouldBe(new PortfolioSlice(PortfolioGroup.Stocks, 0, 0, 0, 0, 50000));
        Asset(y2025, Mxrf11).WeightBasisPoints.ShouldBe(10000);

        y2026.RealizedCents.ShouldBe(-500);
        y2026.Assets.Select(a => a.AssetId).ShouldBe([Mxrf11]);
    }

    // Venda em 31/12 é do ano que termina; em 1º/01, do ano que começa.
    [Fact]
    public void Sale_on_the_edges_of_the_year()
    {
        TradeNote[] notes = [Buy("2024-02-01", Itub3, 100, 2000), Sell("2024-12-31", Itub3, 10, 2100), Sell("2025-01-01", Itub3, 10, 2200)];

        Snapshot(2024, notes).Value.RealizedCents.ShouldBe(1000);
        Snapshot(2025, notes).Value.RealizedCents.ShouldBe(2000);
    }

    // Sem quantidade e sem nada no ano, o ativo fica de fora; com provento no ano (mesmo sem nota), entra com peso zero.
    [Fact]
    public void Who_is_listed()
    {
        TradeNote[] notes = [Buy("2024-02-01", Itub3, 10, 2000), Sell("2024-03-01", Itub3, 10, 2000)];

        var snapshot = Snapshot(2026, notes, [Payout("2026-04-01", Bbas3, 500)]).Value;

        snapshot.Assets.Select(a => (a.AssetId, a.Totals, a.PayoutsCents)).ShouldBe([(Bbas3, PositionTotals.Empty, 500L)]);
        snapshot.InvestedCents.ShouldBe(0);
        snapshot.Groups.ShouldBe([new PortfolioSlice(PortfolioGroup.Stocks, 0, 0, 0, 500, 0)]);
    }

    [Fact]
    public void Year_after_the_current_is_refused()
    {
        var result = Snapshot(2027, []);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe("Escolha um ano até 2026.");
    }

    [Fact]
    public void Year_before_1900_is_refused() =>
        Snapshot(1899, []).Error!.Message.ShouldBe("Escolha um ano a partir de 1900.");

    [Fact]
    public void Empty_portfolio_has_nothing()
    {
        var snapshot = Snapshot(2026, []).Value;

        (snapshot.InvestedCents, snapshot.PayoutsCents, snapshot.RealizedCents, snapshot.Groups.Count, snapshot.Assets.Count)
            .ShouldBe((0L, 0L, 0L, 0, 0));
    }

    [Theory]
    [InlineData(new long[] { 1, 1, 1 }, new[] { 3334, 3333, 3333 })]
    [InlineData(new long[] { 2, 1 }, new[] { 6667, 3333 })]
    [InlineData(new long[] { 0, 5 }, new[] { 0, 10000 })]
    [InlineData(new long[] { 0, 0 }, new[] { 0, 0 })]
    [InlineData(new long[] { }, new int[] { })]
    // 1/6 = 1666,67 e 5/6 = 8333,33: a sobra vai para a maior fração.
    [InlineData(new long[] { 1, 5 }, new[] { 1667, 8333 })]
    // A maior fração ganha mesmo depois na lista: 3333,33 e 6666,67.
    [InlineData(new long[] { 1, 2 }, new[] { 3333, 6667 })]
    // Duas sobras para três frações: 9090,91, 908,18 e 0,91. Empate de fração: as duas de 0,91 levam.
    [InlineData(new long[] { 10000, 999, 1 }, new[] { 9091, 908, 1 })]
    public void Weights_close_100_percent_by_the_largest_remainder(long[] values, int[] expected) =>
        PortfolioWeights.BasisPoints(values).ShouldBe(expected);

    [Fact]
    public void Weights_always_add_to_100_percent_and_stay_within_one_hundredth_of_a_point() =>
        Gen.Long[0, 10_000_000_000].Array[1, 30].Sample(values =>
        {
            var weights = PortfolioWeights.BasisPoints(values);
            var total = values.Sum();
            weights.Length.ShouldBe(values.Length);
            weights.Sum().ShouldBe(total == 0 ? 0 : 10000);
            for (var i = 0; i < values.Length; i++)
            {
                var exact = total == 0 ? 0m : (decimal)values[i] * 10000 / total;
                Math.Abs(weights[i] - exact).ShouldBeLessThan(1m);
            }
        });
}
