using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Recurrences;

// Uma ocorrência prevista: ainda não gerada, e nunca gravada. No cartão, a fatura em que cairia e o
// vencimento dela (o caixa); fora do cartão, o caixa é a própria data.
public sealed record ProjectedOccurrence(
    Guid RecurrenceId,
    Guid AccountId,
    TransactionType Type,
    long AmountCents,
    DateOnly OccurrenceDate,
    DateOnly SettlementDate,
    string? StatementReference);

// A previsão dos lançamentos que se repetem (docs/fase-2.md, 2.14, regra 11): as ocorrências depois de
// GeneratedThrough (nem antes, para não contar duas vezes, nem a partir de amanhã, para não haver buraco
// entre a meia-noite e a próxima execução) e até "until". Segue o que a geração faria: conta inativa não
// gera (regra 8), e a cobrança que cairia numa fatura paga vira pendência, não lançamento (regra 9). A
// fatura sai da regra de sempre, sem abrir nenhuma. No débito automático, o caixa é a data do débito (2.15,
// regra 9).
public static class RecurrenceProjection
{
    // accounts: as contas das séries; statements: as faturas dos cartões delas.
    public static IReadOnlyList<ProjectedOccurrence> Of(
        IEnumerable<Recurrence> recurrences, IReadOnlyCollection<Account> accounts, IReadOnlyCollection<Statement> statements, DateOnly until)
    {
        var byId = accounts.ToDictionary(a => a.Id);
        var projected = new List<ProjectedOccurrence>();

        foreach (var r in recurrences)
        {
            if (!byId.TryGetValue(r.AccountId, out var account) || !account.IsActive)
                continue;

            foreach (var date in RecurrenceSchedule.Between(r.StartDate, r.Frequency, r.EndDate, r.GeneratedThrough, until))
            {
                if (account.Type != AccountType.CreditCard)
                {
                    projected.Add(new ProjectedOccurrence(r.Id, account.Id, r.Type, r.AmountCents, date, r.TransactionDateOf(date), null));
                    continue;
                }

                var statement = new CardPurchase.StatementPlacement(account, statements, date).For(1);
                if (!statement.IsPaid)
                    projected.Add(new ProjectedOccurrence(r.Id, account.Id, r.Type, r.AmountCents, date, statement.DueDate, statement.Reference));
            }
        }

        return projected;
    }

    // "Previsto até o fechamento" de uma fatura: as cobranças que ainda vão cair nela.
    public static long ForStatement(IEnumerable<ProjectedOccurrence> projected, Guid cardId, string reference) =>
        projected.Where(p => p.AccountId == cardId && p.StatementReference == reference).Sum(p => p.AmountCents);
}
