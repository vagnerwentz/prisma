namespace Prisma.Domain.Recurrences;

// Agenda de uma série que se repete (docs/fase-2.md, 2.14, regra 3). A partida é a ocorrência 0: o
// lançamento que criou a série. Toda ocorrência é calculada a partir da partida, nunca da anterior:
// "todo dia 31" cai em 30/11 e volta a 31/12, em vez de ficar em 30 para sempre. O dia que o mês não
// tem vira o último dia do mês (como o cartão, docs/fase-1.md, 2.1, regra 2). Fim de semana e feriado
// não mudam a data: o Pix funciona todos os dias, e a cobrança no cartão é uma compra.
public static class RecurrenceSchedule
{
    // A k-ésima ocorrência (k = 0 é a partida).
    public static DateOnly Occurrence(DateOnly start, RecurrenceFrequency frequency, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (frequency == RecurrenceFrequency.Weekly)
            return start.AddDays(7 * index);

        var month = MonthIndex(start) + index;
        var (year, monthOfYear) = (month / 12, month % 12 + 1);
        return new DateOnly(year, monthOfYear, Math.Min(start.Day, DateTime.DaysInMonth(year, monthOfYear)));
    }

    // As ocorrências depois de "after" e até "until", inclusive, sem passar do término (inclusive). Na
    // geração, "after" é o dia até onde a série já foi gerada e "until" é hoje.
    public static IEnumerable<DateOnly> Between(
        DateOnly start, RecurrenceFrequency frequency, DateOnly? end, DateOnly after, DateOnly until)
    {
        // Começa perto de "after": nenhuma ocorrência anterior a este índice passa de "after".
        var first = frequency == RecurrenceFrequency.Weekly
            ? (after.DayNumber - start.DayNumber) / 7
            : MonthIndex(after) - MonthIndex(start);

        for (var index = Math.Max(first, 0); ; index++)
        {
            var date = Occurrence(start, frequency, index);
            if (date > until || date > end)
                yield break;
            if (date > after)
                yield return date;
        }
    }

    // A primeira ocorrência depois de "after", ou nenhuma se a série já terminou.
    public static DateOnly? Next(DateOnly start, RecurrenceFrequency frequency, DateOnly? end, DateOnly after)
    {
        foreach (var date in Between(start, frequency, end, after, DateOnly.MaxValue))
            return date;
        return null;
    }

    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
}
