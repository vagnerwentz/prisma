using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Recurrences;

// "Se repete": a frequência e o término opcional; a data do lançamento é a partida (docs/fase-2.md, 2.14).
public sealed record RecurrenceRequest(RecurrenceFrequency Frequency, DateOnly? EndDate);

public sealed record RecurrenceResponse(
    Guid Id,
    Guid AccountId,
    TransactionType Type,
    long AmountCents,
    Guid? CategoryId,
    string Description,
    PaymentMethod Method,
    RecurrenceFrequency Frequency,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly GeneratedThrough,
    // Nula quando nada mais será gerado: a série está encerrada.
    DateOnly? NextOccurrence,
    bool IsEnded,
    // Cobranças que cairiam numa fatura já paga, esperando a pessoa decidir (regra 9).
    IReadOnlyList<RecurrencePendingResponse> Pendings)
{
    public static RecurrenceResponse From(Recurrence r, IEnumerable<RecurrencePending>? pendings = null) =>
        new(r.Id, r.AccountId, r.Type, r.AmountCents, r.CategoryId, r.Description, r.Method, r.Frequency,
            r.StartDate, r.EndDate, r.GeneratedThrough, r.NextOccurrence, r.NextOccurrence is null,
            (pendings ?? []).OrderBy(p => p.OccurrenceDate).Select(RecurrencePendingResponse.From).ToList());
}

// O cartão e o valor são os do dia em que a cobrança venceu; a fatura, a que estava paga.
public sealed record RecurrencePendingResponse(
    Guid Id, Guid AccountId, long AmountCents, DateOnly OccurrenceDate, string StatementReference)
{
    public static RecurrencePendingResponse From(RecurrencePending p) =>
        new(p.Id, p.AccountId, p.AmountCents, p.OccurrenceDate, p.StatementReference);
}
