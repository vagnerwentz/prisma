using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Domain.Investments;

// docs/investimentos.md, "I.7c2": a pessoa confere o que um evento da empresa fez com a quantidade dela.
public enum EventReviewVerdict { Confirmed, Disputed }

// A conferência de um evento por uma pessoa: o veredito, a quantidade que a corretora mostra (só no "Não confere") e a
// versão do evento conferida. Um por evento por pessoa, contando os desfeitos: conferir de novo devolve o mesmo registro.
// Se a curadoria corrigir o evento depois, a versão não bate mais e ele volta a pedir conferência.
public sealed class EventReview : Entity
{
    public const long MaxReportedQuantity = 100_000_000;

    private EventReview() { }

    public Guid CorporateEventId { get; private init; }

    public EventReviewVerdict Verdict { get; private set; }

    public long? ReportedQuantity { get; private set; }

    // O UpdatedAt do evento quando foi conferido.
    public DateTime EventVersion { get; private set; }

    public static Result<EventReview> Create(Guid userId, CorporateEvent e, EventReviewVerdict verdict, long? reportedQuantity)
    {
        var review = new EventReview { UserId = userId, CorporateEventId = e.Id };
        var applied = review.Review(e, verdict, reportedQuantity);
        return applied.IsSuccess ? review : applied.Error;
    }

    public Result<EventReview> Review(CorporateEvent e, EventReviewVerdict verdict, long? reportedQuantity)
    {
        if (reportedQuantity is not null && verdict != EventReviewVerdict.Disputed)
            return new Error(ErrorType.Validation, "A quantidade da corretora só vai no \"Não confere\".");
        if (reportedQuantity is < 0 or > MaxReportedQuantity)
            return new Error(ErrorType.Validation, "A quantidade vai de 0 a 100.000.000.");

        ClearDeletion();
        Verdict = verdict;
        ReportedQuantity = reportedQuantity;
        EventVersion = e.UpdatedAt;
        return this;
    }

    public bool Covers(CorporateEvent e) => CorporateEventId == e.Id && EventVersion == e.UpdatedAt;
}

// O que um evento fez com a quantidade da pessoa: antes e depois (no desdobramento, a do próprio ativo; na conversão e
// na liquidação, as cotas recebidas do destino).
public sealed record EventEffect(CorporateEvent Event, Guid AssetId, long QuantityBefore, long QuantityAfter);

public static class EventEffects
{
    // Os eventos com data até hoje que deixaram rastro na posição, do mais antigo ao mais novo. Na conversão e na
    // liquidação, só a origem: o destino é o mesmo evento.
    public static IReadOnlyList<EventEffect> Of(IReadOnlyList<AssetPosition> positions, DateOnly today) =>
        positions
            .SelectMany(p => p.History
                .Where(s => s.Event is not null && s.Date <= today
                            && s.Kind is PositionStepKind.Split or PositionStepKind.ConversionOut or PositionStepKind.LiquidationOut)
                .Select(s => new EventEffect(s.Event!, p.AssetId, s.Quantity,
                    s.Kind == PositionStepKind.Split ? s.After.Quantity : s.Event!.Convert(s.Quantity))))
            .OrderBy(e => e.Event.Date)
            .ThenBy(e => e.Event.Key, StringComparer.Ordinal)
            .ToList();

    // Os que ainda pedem conferência: sem conferência que cubra a versão atual do evento.
    public static IReadOnlyList<EventEffect> Pending(IReadOnlyList<EventEffect> effects, IEnumerable<EventReview> reviews)
    {
        var byEvent = reviews.ToDictionary(r => r.CorporateEventId);
        return effects.Where(e => !byEvent.TryGetValue(e.Event.Id, out var r) || !r.Covers(e.Event)).ToList();
    }
}
