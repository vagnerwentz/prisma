namespace Prisma.Domain.Recurrences;

// Ocorrência que cairia numa fatura já paga, guardada até a pessoa decidir (docs/fase-2.md, 2.14,
// regra 9). Resolver a pendência a exclui (soft delete).
//
// Guarda o cartão e o valor do dia em que venceu: editar a série vale do próximo em diante (regra 6), e a
// cobrança pendente é anterior à edição.
public sealed class RecurrencePending : Entity
{
    private RecurrencePending() { }

    public Guid RecurrenceId { get; private set; }
    public Guid AccountId { get; private set; }
    public long AmountCents { get; private set; }
    public DateOnly OccurrenceDate { get; private set; }
    public string StatementReference { get; private set; } = "";

    public static RecurrencePending For(Recurrence recurrence, PendingOccurrence pending) =>
        new()
        {
            UserId = recurrence.UserId,
            RecurrenceId = recurrence.Id,
            AccountId = recurrence.AccountId,
            AmountCents = recurrence.AmountCents,
            OccurrenceDate = pending.OccurrenceDate,
            StatementReference = pending.StatementReference,
        };
}
