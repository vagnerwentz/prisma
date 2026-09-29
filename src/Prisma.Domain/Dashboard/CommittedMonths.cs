using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Dashboard;

// ProjectedExpenseCents: a parte prevista das despesas que se repetem (docs/fase-2.md, 2.14, regra 11), à
// parte do que já existe.
public readonly record struct CommittedMonth(DateOnly Month, long ExpenseCents, long ProjectedExpenseCents = 0);

// Já comprometido (docs/fase-2.md, 2.6): para os 6 meses seguintes ao de hoje, o "Saiu" que o Resumo
// de cada um já mostra hoje, pela mesma regra do resumo (estornos abatidos, transferência fora). A
// última parcela vale mesmo além dos 6 meses, e só se vencer depois do mês de hoje.
public sealed record CommittedMonths(IReadOnlyList<CommittedMonth> Months, DateOnly? LastInstallmentMonth)
{
    public const int Horizon = 6;

    // projected: a previsão das séries; cada despesa conta no mês do caixa (no cartão, o do vencimento).
    public static CommittedMonths Of(
        DateOnly today, IEnumerable<MonthlyEntry> entries, DateOnly? lastInstallmentDue, IEnumerable<ProjectedOccurrence>? projected = null)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var byMonth = entries.ToLookup(e => e.Month, e => e.Entry);
        var expected = (projected ?? [])
            .Where(p => p.Type == TransactionType.Expense)
            .ToLookup(p => new DateOnly(p.SettlementDate.Year, p.SettlementDate.Month, 1), p => p.AmountCents);

        var months = Enumerable.Range(1, Horizon)
            .Select(offset => thisMonth.AddMonths(offset))
            .Select(month => new CommittedMonth(month, MonthlySummary.Of(byMonth[month]).ExpenseCents, expected[month].Sum()))
            .ToList();

        DateOnly? last = lastInstallmentDue is { } due && due >= thisMonth.AddMonths(1)
            ? new DateOnly(due.Year, due.Month, 1)
            : null;
        return new CommittedMonths(months, last);
    }
}
