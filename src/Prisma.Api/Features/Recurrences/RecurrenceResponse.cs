using Prisma.Domain;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Recurrences;

// "Se repete": a frequência e o término opcional; a data do lançamento é a partida (docs/fase-2.md, 2.14).
// Débito automático (2.15): AutoDebit, AmountVaries (o valor é a estimativa) e DueDate (o vencimento da
// primeira ocorrência, até 7 dias antes da data do lançamento). Os três são opcionais: sem eles, a série
// comum de antes.
public sealed record RecurrenceRequest(
    RecurrenceFrequency Frequency, DateOnly? EndDate, bool? AutoDebit = null, bool? AmountVaries = null, DateOnly? DueDate = null)
{
    public AutoDebitTerms? AutoDebitTerms => AutoDebit == true ? new AutoDebitTerms(AmountVaries ?? false, DueDate) : null;

    // Valor que muda e vencimento só existem no débito automático.
    public Error? CheckAutoDebitFields() =>
        AutoDebit == true ? null
        : AmountVaries == true ? RecurrenceErrors.AmountVariesNeedsAutoDebit
        : DueDate is not null ? new Error(ErrorType.Validation, "Só o débito automático tem vencimento.")
        : null;
}

public static class RecurrenceErrors
{
    public static readonly Error AmountVariesNeedsAutoDebit = new(ErrorType.Validation, "Só o débito automático tem valor que muda.");
}

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
    IReadOnlyList<RecurrencePendingResponse> Pendings,
    // Débito automático (2.15): o tipo, se o valor é estimativa e a data do próximo lançamento (no débito
    // automático, a do débito; nas outras, a própria NextOccurrence).
    RecurrenceKind Kind,
    bool AmountVaries,
    DateOnly? NextTransactionDate)
{
    public static RecurrenceResponse From(Recurrence r, IEnumerable<RecurrencePending>? pendings = null) =>
        new(r.Id, r.AccountId, r.Type, r.AmountCents, r.CategoryId, r.Description, r.Method, r.Frequency,
            r.StartDate, r.EndDate, r.GeneratedThrough, r.NextOccurrence, r.NextOccurrence is null,
            (pendings ?? []).OrderBy(p => p.OccurrenceDate).Select(RecurrencePendingResponse.From).ToList(),
            r.Kind, r.AmountVaries, r.NextTransactionDate);
}

// O cartão e o valor são os do dia em que a cobrança venceu; a fatura, a que estava paga.
public sealed record RecurrencePendingResponse(
    Guid Id, Guid AccountId, long AmountCents, DateOnly OccurrenceDate, string StatementReference)
{
    public static RecurrencePendingResponse From(RecurrencePending p) =>
        new(p.Id, p.AccountId, p.AmountCents, p.OccurrenceDate, p.StatementReference);
}
