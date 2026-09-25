namespace Prisma.Domain.Dashboard;

// Soma agrupada de um mês (Month é o primeiro dia dele).
public readonly record struct MonthlyEntry(DateOnly Month, SummaryEntry Entry);

public sealed record MonthInHistory(DateOnly Month, MonthlySummary Summary);

// Comparativo mensal (docs/fase-2.md, 2.3): os meses que terminam no escolhido, do mais antigo ao
// mais recente, cada um com as regras do resumo (2.1). Mês sem lançamentos sai com zeros.
public static class MonthlyHistory
{
    public const int Months = 6;

    public static DateOnly FirstMonth(DateOnly lastMonth) => lastMonth.AddMonths(-(Months - 1));

    public static IReadOnlyList<MonthInHistory> Of(DateOnly lastMonth, IEnumerable<MonthlyEntry> entries)
    {
        if (lastMonth.Day != 1)
            throw new ArgumentException("O mês deve ser o primeiro dia dele.", nameof(lastMonth));

        var byMonth = entries.ToLookup(e => e.Month, e => e.Entry);
        return Enumerable.Range(0, Months)
            .Select(i => FirstMonth(lastMonth).AddMonths(i))
            .Select(month => new MonthInHistory(month, MonthlySummary.Of(byMonth[month])))
            .ToList();
    }
}
