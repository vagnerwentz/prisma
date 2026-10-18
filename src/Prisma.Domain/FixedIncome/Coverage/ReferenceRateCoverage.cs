using Prisma.Domain.Calendar;

namespace Prisma.Domain.FixedIncome.Coverage;

// O estado do histórico de taxas gravado: de onde a onde vai, se falta algum dia útil no meio (lacuna) e se está atrasado em
// relação a hoje (defasagem). docs/renda-fixa.md, 3.3.
//
// É o único lugar do cálculo de renda fixa que usa o calendário bancário, e só para DIAGNOSTICAR (decisão do dono, D8): o
// rendimento nunca o consulta, usa apenas as datas que o Banco Central publicou. Por isso este tipo mora no próprio namespace,
// que o teste de arquitetura libera sozinho. Conferido com dados reais: de 02/01/2023 a 15/10/2026 o Banco Central publicou 951
// datas e nenhum dia útil do calendário ficou sem taxa (só o feriado de 20/11/2023, que ainda não era nacional, tem taxa).
public sealed record ReferenceRateCoverage(
    DateOnly? First, DateOnly? Last, IReadOnlyList<DateOnly> MissingBusinessDays, DateOnly ExpectedLast, bool IsStale)
{
    public bool IsComplete => MissingBusinessDays.Count == 0;

    // O CDI de um dia sai à noite, depois do fechamento. Então de manhã o que deve estar gravado é o do último dia útil
    // ANTES de hoje; o de hoje ainda não saiu.
    public static DateOnly ExpectedLastDate(DateOnly today)
    {
        var date = today.AddDays(-1);
        while (!BankCalendar.IsBusinessDay(date))
            date = date.AddDays(-1);
        return date;
    }

    public static ReferenceRateCoverage Analyze(IEnumerable<DateOnly> storedDates, DateOnly today)
    {
        var stored = storedDates.ToHashSet();
        var expected = ExpectedLastDate(today);
        if (stored.Count == 0)
            return new ReferenceRateCoverage(null, null, [], expected, IsStale: true);

        var first = stored.Min();
        var last = stored.Max();
        var missing = new List<DateOnly>();
        for (var date = first; date < last; date = date.AddDays(1))
            if (BankCalendar.IsBusinessDay(date) && !stored.Contains(date))
                missing.Add(date);

        return new ReferenceRateCoverage(first, last, missing, expected, IsStale: last < expected);
    }
}
