using CsCheck;
using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7c4": liquidação paga em cotas. A origem sai como venda (cotas a preço de referência, fração
// e caixa); o destino entra como compra pelo preço de referência. Os números vêm do fato relevante do BCFF11 → BTHF11
// (dezembro/2024): 0,8 cota de BTHF11 a R$ 10,25 e R$ 0,56 de caixa por cota de BCFF11.
public sealed class LiquidationInPositionTests
{
    private static readonly Guid Bcff11 = Guid.CreateVersion7();
    private static readonly Guid Bthf11 = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2024, 12, 16);

    // R$ 10,25 e R$ 0,56 em centésimos de milionésimo de real.
    private const long Price = 10_25_000_000;
    private const long Cash = 56_000_000;

    private static string Symbol(Guid id) => id == Bcff11 ? "BCFF11" : "BTHF11";

    private static TradeNote Note(DateOnly date, Guid asset, TradeSide side, long quantity, long price) =>
        TradeNote.Create(Guid.CreateVersion7(), "Íon", date, [new Trade(asset, AssetKind.Fii, side, quantity, price)], 0, 0, TradeNote.StockOnly).Value;

    private static CorporateEvent Liquidation(long price = Price, long? cash = Cash, int from = 100, int to = 80) =>
        CorporateEvent.Create("liq", CorporateEventKind.Liquidation, Bcff11, Bthf11, Day, from, to, "teste", Now, price, cash).Value;

    private static IReadOnlyList<AssetPosition> Build(TradeNote[] notes, CorporateEvent? e = null) =>
        PositionCalculator.Build(notes, [e ?? Liquidation()], Symbol).Value;

    private static AssetPosition Of(IReadOnlyList<AssetPosition> positions, Guid asset) => positions.Single(p => p.AssetId == asset);

    // O caso do dono: 896 cotas a R$ 8,42 (R$ 7.544,32). 716 cotas a R$ 10,25 = R$ 7.339,00; fração 0,8 × R$ 10,25 = R$ 8,20;
    // caixa 896 × R$ 0,56 = R$ 501,76. Recebido R$ 7.848,96; resultado +R$ 304,64.
    [Fact]
    public void Owner_case_the_new_quotas_cost_the_reference_price()
    {
        var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 896, 842)]);

        var bthf = Of(positions, Bthf11);
        bthf.Current.ShouldBe(new PositionTotals(716, 733900, 733900));
        bthf.Current.AveragePriceCents.ShouldBe(1025);
        bthf.Current.AverageCostCents.ShouldBe(1025);

        var bcff = Of(positions, Bcff11);
        bcff.Current.ShouldBe(PositionTotals.Empty);
        bcff.RealizedCents.ShouldBe(30464);
        var step = bcff.History.Last();
        (step.Kind, step.Date, step.Quantity, step.GrossCents, step.RealizedCents, step.RelatedAssetId)
            .ShouldBe((PositionStepKind.LiquidationOut, Day, 896L, 784896L, (long?)30464, (Guid?)Bthf11));
        var inStep = bthf.History.Single();
        (inStep.Kind, inStep.Quantity, inStep.GrossCents, inStep.RelatedAssetId).ShouldBe((PositionStepKind.LiquidationIn, 716L, 733900L, (Guid?)Bcff11));
    }

    // O exemplo do documento: 53 cotas a R$ 7,51 → 42 BTHF11 (R$ 430,50), fração 0,40 (R$ 4,10), caixa R$ 29,68.
    [Fact]
    public void Example_of_the_document()
    {
        var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 53, 751)]);

        Of(positions, Bthf11).Current.ShouldBe(new PositionTotals(42, 43050, 43050));
        var step = Of(positions, Bcff11).History.Last();
        step.GrossCents.ShouldBe(46428); // 430,50 + 4,10 + 29,68
        step.RealizedCents.ShouldBe(46428 - 39803);
    }

    // Caso 2 do documento: quem já tinha 100 BTHF11 a R$ 10,00 e recebe 50 a R$ 10,25 fica com 150 a R$ 10,08.
    [Fact]
    public void Case_2_of_the_document_weights_the_average()
    {
        var positions = Build(
        [
            Note(new DateOnly(2024, 1, 10), Bthf11, TradeSide.Buy, 100, 1000),
            Note(new DateOnly(2024, 1, 10), Bcff11, TradeSide.Buy, 63, 800), // 63 × 0,8 = 50,4 → 50
        ]);

        var bthf = Of(positions, Bthf11).Current;
        bthf.ShouldBe(new PositionTotals(150, 151250, 151250));
        bthf.AveragePriceCents.ShouldBe(1008);
        // Fração 0,4 × 10,25 = 4,10; caixa 63 × 0,56 = 35,28.
        Of(positions, Bcff11).History.Last().GrossCents.ShouldBe(51250 + 410 + 3528);
    }

    [Fact]
    public void Without_cash_and_without_fraction()
    {
        var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 100, 800)], Liquidation(cash: null));

        Of(positions, Bthf11).Current.ShouldBe(new PositionTotals(80, 82000, 82000));
        Of(positions, Bcff11).RealizedCents.ShouldBe(82000 - 80000);
    }

    // Venda com perda também é resultado.
    [Fact]
    public void Loss_is_a_negative_result() =>
        Of(Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 100, 1000)]), Bcff11).RealizedCents.ShouldBe(82000 + 5600 - 100000);

    // Preço com mais casas que centavos (o do IRIM11): só o produto vira centavo, meio para cima.
    [Fact]
    public void Price_with_eight_decimals_rounds_only_the_product()
    {
        var irim = CorporateEvent.Create("liq", CorporateEventKind.Liquidation, Bcff11, Bthf11, Day, 1_000_000_000, 884_794_245, "teste", Now,
            83_39460760, 4_30605192).Value;

        var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 100, 5000)], irim);

        // 100 × 0,884794245 = 88,4794245 → 88 cotas × 83,3946076 = 7.338,725… → R$ 7.338,73.
        Of(positions, Bthf11).Current.ShouldBe(new PositionTotals(88, 733873, 733873));
        // Fração 0,4794245 × 83,3946076 = 39,98… → R$ 39,98; caixa 100 × 4,30605192 = R$ 430,61.
        Of(positions, Bcff11).History.Last().GrossCents.ShouldBe(733873 + 3998 + 43061);
    }

    // Pouca cota: 2 × 0,8 = 1,6 → recebe 1 cota; 1 × 0,8 = 0,8 → nenhuma, e tudo vira dinheiro (fração e caixa).
    [Fact]
    public void One_quota_received_or_none()
    {
        Of(Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 2, 800)]), Bthf11).Current.ShouldBe(new PositionTotals(1, 1025, 1025));

        var none = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 1, 800)]);
        none.Select(p => p.AssetId).ShouldBe([Bcff11]);
        Of(none, Bcff11).History.Last().GrossCents.ShouldBe(820 + 56);
    }

    [Fact]
    public void Without_a_position_the_event_leaves_no_trace() =>
        Build([Note(new DateOnly(2023, 3, 10), Bthf11, TradeSide.Buy, 10, 1000)]).Single().History.Count.ShouldBe(1);

    // Vale no fim do dia: quem compra na data entra; no dia seguinte, a nota da origem é recusada.
    [Fact]
    public void Applies_at_the_end_of_the_day_and_refuses_later_notes()
    {
        Of(Build([Note(Day, Bcff11, TradeSide.Buy, 100, 800)]), Bthf11).Current.Quantity.ShouldBe(80);

        var later = PositionCalculator.Build([Note(Day.AddDays(1), Bcff11, TradeSide.Buy, 100, 800)], [Liquidation()], Symbol);
        later.IsSuccess.ShouldBeFalse();
        later.Error.Message.ShouldBe("BCFF11 virou BTHF11 em 16/12/2024. Desde então, lance como BTHF11.");
    }

    // A venda do destino depois usa o custo novo.
    [Fact]
    public void Later_sale_of_the_target_uses_the_new_cost()
    {
        var positions = Build(
        [
            Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 896, 842),
            Note(new DateOnly(2025, 3, 3), Bthf11, TradeSide.Sell, 716, 900),
        ]);

        Of(positions, Bthf11).RealizedCents.ShouldBe(716 * 900 - 733900);
    }

    // A carteira do ano (I.7e): o resultado fica em 2024, o ano da liquidação.
    [Fact]
    public void Result_falls_in_the_year_of_the_liquidation()
    {
        var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, 896, 842)]);

        var y2024 = PortfolioSnapshot.Build(2024, new DateOnly(2026, 10, 9), positions, [], _ => AssetKind.Fii, Symbol).Value;
        y2024.RealizedCents.ShouldBe(30464);
        y2024.InvestedCents.ShouldBe(733900);
    }

    // O que sai da origem mais o resultado é sempre o valor recebido; o destino recebe exatamente o valor das cotas.
    [Fact]
    public void What_leaves_plus_the_result_is_what_was_received() =>
        Gen.Select(Gen.Long[1, 1_000_000], Gen.Long[1, 100_000], Gen.Int[1, 1000], Gen.Int[1, 1000], Gen.Long[1, 100_000_000_000], Gen.Long[0, 10_000_000_000])
            .Sample((quantity, price, from, to, unit, cash) =>
            {
                var e = Liquidation(unit, cash, from, to);
                var positions = Build([Note(new DateOnly(2023, 3, 10), Bcff11, TradeSide.Buy, quantity, price)], e);
                var bcff = Of(positions, Bcff11);
                var step = bcff.History.Last();
                step.GrossCents.ShouldBe(bcff.History[0].After.CostBasisCents + bcff.RealizedCents);
                var received = e.Convert(quantity);
                if (received == 0)
                    return;
                var bthf = Of(positions, Bthf11).Current;
                bthf.Quantity.ShouldBe(received);
                bthf.CostBasisCents.ShouldBe(bthf.GrossCostCents);
                bthf.CostBasisCents.ShouldBeLessThanOrEqualTo(step.GrossCents);
            });
}
