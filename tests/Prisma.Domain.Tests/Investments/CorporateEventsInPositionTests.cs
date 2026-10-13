using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7c1": desdobramento, grupamento e conversão valem sozinhos no fim do dia da data, para
// quem tinha o ativo. O caso real do dono: BCFF11 (compras de 2020 e 2023), desdobramento 1:8 em 28/11/2023 e
// conversão 100:80 em BTHF11 em 16/12/2024.
public sealed class CorporateEventsInPositionTests
{
    private static readonly Guid Bcff11 = Guid.CreateVersion7();
    private static readonly Guid Bthf11 = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly SplitDay = new(2023, 11, 28);
    private static readonly DateOnly ConversionDay = new(2024, 12, 16);

    private static string Symbol(Guid id) => id == Bcff11 ? "BCFF11" : "BTHF11";

    private static TradeNote Note(DateOnly date, Guid asset, TradeSide side, long quantity, long price) =>
        TradeNote.Create(Guid.CreateVersion7(), "Íon", date, [new Trade(asset, AssetKind.Fii, side, quantity, price)], 0, 0, TradeNote.StockOnly).Value;

    private static CorporateEvent Split(DateOnly date, int from, int to, Guid? asset = null) =>
        CorporateEvent.Create($"split-{date}", CorporateEventKind.Split, asset ?? Bcff11, null, date, from, to, "teste", Now).Value;

    private static CorporateEvent Conversion(DateOnly date, int from, int to) =>
        CorporateEvent.Create($"conv-{date}", CorporateEventKind.Conversion, Bcff11, Bthf11, date, from, to, "teste", Now).Value;

    private static Result<IReadOnlyList<AssetPosition>> Build(TradeNote[] notes, CorporateEvent[] events) =>
        PositionCalculator.Build(notes, events, Symbol);

    private static AssetPosition Of(IReadOnlyList<AssetPosition> positions, Guid asset) => positions.Single(p => p.AssetId == asset);

    private static readonly TradeNote[] OwnerNotes =
    [
        Note(new DateOnly(2020, 9, 30), Bcff11, TradeSide.Buy, 10, 9028),
        Note(new DateOnly(2023, 10, 2), Bcff11, TradeSide.Buy, 100, 7219),
    ];

    [Fact]
    public void Owner_case_ends_in_BTHF11_with_the_whole_cost()
    {
        var positions = Build(OwnerNotes, [Split(SplitDay, 1, 8), Conversion(ConversionDay, 100, 80)]).Value;

        // 110 cotas → 880 no desdobramento → 704 BTHF11. Custo: 10 × 90,28 + 100 × 72,19 = R$ 8.121,80.
        Of(positions, Bthf11).Current.ShouldBe(new PositionTotals(704, 812180, 812180));
        Of(positions, Bthf11).Current.AveragePriceCents.ShouldBe(1154); // 11,5367
        Of(positions, Bcff11).Current.ShouldBe(PositionTotals.Empty);
        Of(positions, Bcff11).RealizedCents.ShouldBe(0); // conversão não é venda
    }

    [Fact]
    public void Split_multiplies_the_quantity_and_keeps_the_cost()
    {
        var bcff = Of(Build(OwnerNotes, [Split(SplitDay, 1, 8)]).Value, Bcff11);

        bcff.Current.ShouldBe(new PositionTotals(880, 812180, 812180));
        bcff.Current.AveragePriceCents.ShouldBe(923); // 9,2293
        var step = bcff.History.Last();
        (step.Kind, step.Date, step.Quantity, step.After.Quantity, step.Note).ShouldBe((PositionStepKind.Split, SplitDay, 110L, 880L, (TradeNote?)null));
    }

    // Quem compra na data-com tem direito; quem compra no dia seguinte, não.
    [Fact]
    public void Event_applies_at_the_end_of_its_day()
    {
        var onTheDay = Of(Build([Note(SplitDay, Bcff11, TradeSide.Buy, 10, 7000)], [Split(SplitDay, 1, 8)]).Value, Bcff11);
        var dayAfter = Of(Build([Note(SplitDay.AddDays(1), Bcff11, TradeSide.Buy, 10, 900)], [Split(SplitDay, 1, 8)]).Value, Bcff11);

        onTheDay.Current.Quantity.ShouldBe(80);
        dayAfter.Current.Quantity.ShouldBe(10);
    }

    // Lançando as notas em ordem: a venda das 880 depois do desdobramento passa.
    [Fact]
    public void Sale_after_the_split_uses_the_new_quantity() =>
        Build([.. OwnerNotes, Note(new DateOnly(2024, 3, 1), Bcff11, TradeSide.Sell, 880, 1000)], [Split(SplitDay, 1, 8)])
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void Reverse_split_rounds_down_and_keeps_the_cost()
    {
        var position = Of(Build([Note(new DateOnly(2024, 1, 5), Bcff11, TradeSide.Buy, 105, 100)], [Split(SplitDay.AddYears(1), 10, 1)]).Value, Bcff11);

        position.Current.ShouldBe(new PositionTotals(10, 10500, 10500));
        position.Current.AveragePriceCents.ShouldBe(1050);
    }

    [Fact]
    public void Conversion_adds_to_a_position_already_held_in_the_target()
    {
        var notes = OwnerNotes.Append(Note(new DateOnly(2024, 11, 1), Bthf11, TradeSide.Buy, 100, 800)).ToArray();

        var bthf = Of(Build(notes, [Split(SplitDay, 1, 8), Conversion(ConversionDay, 100, 80)]).Value, Bthf11);

        bthf.Current.ShouldBe(new PositionTotals(804, 892180, 892180));
        bthf.History.Select(s => s.Kind).ShouldBe([PositionStepKind.Trade, PositionStepKind.ConversionIn]);
    }

    [Fact]
    public void Conversion_shows_on_both_histories()
    {
        var positions = Build(OwnerNotes, [Split(SplitDay, 1, 8), Conversion(ConversionDay, 100, 80)]).Value;

        var outStep = Of(positions, Bcff11).History.Last();
        var inStep = Of(positions, Bthf11).History.Single();
        (outStep.Kind, outStep.Quantity, outStep.RelatedAssetId, outStep.After.Quantity)
            .ShouldBe((PositionStepKind.ConversionOut, 880L, (Guid?)Bthf11, 0L));
        (inStep.Kind, inStep.Quantity, inStep.RelatedAssetId, inStep.Date).ShouldBe((PositionStepKind.ConversionIn, 704L, (Guid?)Bcff11, ConversionDay));
    }

    [Fact]
    public void Trading_the_old_asset_after_the_conversion_is_refused()
    {
        var late = Note(ConversionDay.AddDays(1), Bcff11, TradeSide.Buy, 100, 700);

        Build([.. OwnerNotes, late], [Conversion(ConversionDay, 100, 80)]).Error!.Message
            .ShouldBe("BCFF11 virou BTHF11 em 16/12/2024. Desde então, lance como BTHF11.");
    }

    // A trava é para depois da data: a nota do próprio dia ainda é do ativo antigo e converte no fim do dia.
    [Fact]
    public void Note_on_the_conversion_day_is_still_accepted() =>
        Of(Build([Note(ConversionDay, Bcff11, TradeSide.Buy, 100, 700)], [Conversion(ConversionDay, 100, 80)]).Value, Bthf11)
            .Current.Quantity.ShouldBe(80);

    // No mesmo dia, o desdobramento vem antes da conversão; ao contrário, ele cairia no ativo já zerado e se perderia.
    [Fact]
    public void Split_comes_before_a_conversion_on_the_same_day() =>
        Of(Build(OwnerNotes, [Conversion(ConversionDay, 100, 80), Split(ConversionDay, 1, 8)]).Value, Bthf11)
            .Current.Quantity.ShouldBe(704);

    [Fact]
    public void Event_without_a_position_leaves_no_trace()
    {
        var positions = Build([Note(new DateOnly(2025, 1, 2), Bthf11, TradeSide.Buy, 10, 800)], [Split(SplitDay, 1, 8), Conversion(ConversionDay, 100, 80)]).Value;

        positions.ShouldHaveSingleItem().AssetId.ShouldBe(Bthf11);
        positions[0].History.ShouldHaveSingleItem().Kind.ShouldBe(PositionStepKind.Trade);
    }

    // Comprado e vendido antes do evento: zerado na data, o evento não aparece.
    [Fact]
    public void Event_after_selling_everything_is_skipped() =>
        Of(Build([.. OwnerNotes, Note(new DateOnly(2023, 11, 1), Bcff11, TradeSide.Sell, 110, 7500)], [Split(SplitDay, 1, 8)]).Value, Bcff11)
            .History.ShouldAllBe(s => s.Kind == PositionStepKind.Trade);
}
