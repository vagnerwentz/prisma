namespace Prisma.Domain.Dashboard;

public readonly record struct CommittedMonth(DateOnly Month, long ExpenseCents);

// Já comprometido (docs/fase-2.md, 2.6): para os 6 meses seguintes ao de hoje, o "Saiu" que o Resumo
// de cada um já mostra hoje, pela mesma regra do resumo (estornos abatidos, transferência fora). A
// última parcela vale mesmo além dos 6 meses, e só se vencer depois do mês de hoje.
public sealed record CommittedMonths(IReadOnlyList<CommittedMonth> Months, DateOnly? LastInstallmentMonth)
{
    public const int Horizon = 6;

    public static CommittedMonths Of(DateOnly today, IEnumerable<MonthlyEntry> entries, DateOnly? lastInstallmentDue)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var byMonth = entries.ToLookup(e => e.Month, e => e.Entry);

        var months = Enumerable.Range(1, Horizon)
            .Select(offset => thisMonth.AddMonths(offset))
            .Select(month => new CommittedMonth(month, MonthlySummary.Of(byMonth[month]).ExpenseCents))
            .ToList();

        DateOnly? last = lastInstallmentDue is { } due && due >= thisMonth.AddMonths(1)
            ? new DateOnly(due.Year, due.Month, 1)
            : null;
        return new CommittedMonths(months, last);
    }
}
