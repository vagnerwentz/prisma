using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, "I.7c2": a pessoa confere, uma vez, o que cada evento fez com a quantidade dela. O que pede
// conferência é calculado da posição; a conferência guarda a versão do evento, e o evento corrigido volta a pedir.
public sealed class EventReviewTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid Snag11 = Guid.CreateVersion7();
    private static readonly Guid Bcff11 = Guid.CreateVersion7();
    private static readonly Guid Bthf11 = Guid.CreateVersion7();
    private static readonly Guid Petz3 = Guid.CreateVersion7();
    private static readonly Guid Auau3 = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly CorporateEvent SnagSplit =
        CorporateEvent.Create("snag", CorporateEventKind.Split, Snag11, null, new DateOnly(2023, 8, 1), 1, 10, "f", Now).Value;

    private static readonly CorporateEvent BcffLiquidation =
        CorporateEvent.Create("bcff", CorporateEventKind.Liquidation, Bcff11, Bthf11, new DateOnly(2024, 12, 16), 100, 80, "f", Now,
            1_025_000_000, 56_000_000).Value;

    private static readonly CorporateEvent PetzConversion =
        CorporateEvent.Create("petz", CorporateEventKind.Conversion, Petz3, Auau3, new DateOnly(2026, 1, 2), 1, 1, "f", Now).Value;

    private static string Symbol(Guid id) => id.ToString();

    private static TradeNote Buy(string date, Guid asset, long quantity, AssetKind kind = AssetKind.Fii) =>
        TradeNote.Create(UserId, "Íon", DateOnly.Parse(date), [new Trade(asset, kind, TradeSide.Buy, quantity, 1000)], 0, 0,
            TradeNote.StockOnly).Value;

    private static IReadOnlyList<EventEffect> Effects(TradeNote[] notes, params CorporateEvent[] events) =>
        EventEffects.Of(PositionCalculator.Build(notes, events, Symbol).Value, Today);

    [Fact]
    public void Each_kind_shows_what_it_did_to_the_quantity()
    {
        var effects = Effects(
            [Buy("2023-03-13", Snag11, 19, AssetKind.FiAgro), Buy("2023-03-10", Bcff11, 896), Buy("2025-05-02", Petz3, 50, AssetKind.Stock)],
            SnagSplit, BcffLiquidation, PetzConversion);

        effects.Select(e => (e.Event.Key, e.AssetId, e.QuantityBefore, e.QuantityAfter)).ShouldBe(
        [
            ("snag", Snag11, 19L, 190L),
            ("bcff", Bcff11, 896L, 716L), // recebeu 716 BTHF11
            ("petz", Petz3, 50L, 50L),
        ]);
    }

    // Só a origem: o destino da conversão não pede uma segunda conferência do mesmo evento.
    [Fact]
    public void Conversion_asks_once_for_the_source()
    {
        var effects = Effects([Buy("2025-05-02", Petz3, 50, AssetKind.Stock), Buy("2025-05-02", Auau3, 10, AssetKind.Stock)], PetzConversion);

        effects.Single().AssetId.ShouldBe(Petz3);
    }

    [Fact]
    public void Without_a_position_on_the_date_nothing_is_asked() =>
        Effects([Buy("2023-08-02", Snag11, 19, AssetKind.FiAgro)], SnagSplit).ShouldBeEmpty();

    // Evento com data depois de hoje (anunciado, ainda não aconteceu) não pede conferência ainda.
    [Fact]
    public void Future_event_is_not_asked_yet()
    {
        var future = CorporateEvent.Create("fut", CorporateEventKind.Split, Snag11, null, Today.AddDays(5), 1, 2, "f", Now).Value;

        Effects([Buy("2023-03-13", Snag11, 19, AssetKind.FiAgro)], future).ShouldBeEmpty();
        Effects([Buy("2023-03-13", Snag11, 19, AssetKind.FiAgro)],
            CorporateEvent.Create("hoje", CorporateEventKind.Split, Snag11, null, Today, 1, 2, "f", Now).Value).Count.ShouldBe(1);
    }

    [Fact]
    public void Reviewed_event_leaves_and_a_corrected_one_comes_back()
    {
        var effects = Effects([Buy("2023-03-13", Snag11, 19, AssetKind.FiAgro), Buy("2023-03-10", Bcff11, 896)], SnagSplit, BcffLiquidation);
        var review = EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Confirmed, null).Value;

        EventEffects.Pending(effects, [review]).Select(e => e.Event.Key).ShouldBe(["bcff"]);

        // A curadoria corrige o evento: ele volta a pedir conferência.
        var corrected = CorporateEvent.Create("snag", CorporateEventKind.Split, Snag11, null, new DateOnly(2023, 8, 1), 1, 10, "f", Now).Value;
        CorporateEventReconciliation.Apply([corrected],
            [new("snag", CorporateEventKind.Split, Snag11, null, new DateOnly(2023, 8, 1), 1, 9, "f, corrigido")], Now.AddDays(1)).Value.Updated.ShouldBe(1);
        var later = Effects([Buy("2023-03-13", Snag11, 19, AssetKind.FiAgro)], corrected);
        EventEffects.Pending(later, [review]).Select(e => e.Event.Key).ShouldBe(["snag"]);
    }

    [Fact]
    public void Review_keeps_the_verdict_the_reported_quantity_and_the_version()
    {
        var disputed = EventReview.Create(UserId, BcffLiquidation, EventReviewVerdict.Disputed, 704).Value;

        (disputed.UserId, disputed.CorporateEventId, disputed.Verdict, disputed.ReportedQuantity, disputed.EventVersion)
            .ShouldBe((UserId, BcffLiquidation.Id, EventReviewVerdict.Disputed, (long?)704, BcffLiquidation.UpdatedAt));
    }

    [Fact]
    public void Reported_quantity_is_only_for_a_dispute_and_within_limits()
    {
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Confirmed, 190).Error!.Message
            .ShouldBe("A quantidade da corretora só vai no \"Não confere\".");
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Disputed, -1).Error!.Message
            .ShouldBe("A quantidade vai de 0 a 100.000.000.");
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Disputed, 100_000_001).IsSuccess.ShouldBeFalse();
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Disputed, 0).IsSuccess.ShouldBeTrue();
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Disputed, 100_000_000).IsSuccess.ShouldBeTrue();
        EventReview.Create(UserId, SnagSplit, EventReviewVerdict.Disputed, null).IsSuccess.ShouldBeTrue();
    }

    // Conferir de novo (depois de desfazer, ou o evento corrigido) devolve o mesmo registro, com o veredito novo. O
    // desfeito não chega aqui: a lista só recebe conferências que valem (o filtro do soft delete).
    [Fact]
    public void Reviewing_again_restores_and_updates_the_same_record()
    {
        var e = CorporateEvent.Create("snag", CorporateEventKind.Split, Snag11, null, new DateOnly(2023, 8, 1), 1, 10, "f", Now).Value;
        var review = EventReview.Create(UserId, e, EventReviewVerdict.Disputed, 200).Value;
        CorporateEventReconciliation.Apply([e], [new("snag", CorporateEventKind.Split, Snag11, null, new DateOnly(2023, 8, 1), 1, 9, "f")],
            Now.AddDays(2));
        review.Covers(e).ShouldBeFalse();

        review.Review(e, EventReviewVerdict.Confirmed, null).IsSuccess.ShouldBeTrue();

        (review.Verdict, review.ReportedQuantity, review.EventVersion).ShouldBe((EventReviewVerdict.Confirmed, (long?)null, e.UpdatedAt));
        review.Covers(e).ShouldBeTrue();
        review.Covers(SnagSplit).ShouldBeFalse(); // outro evento
    }
}
