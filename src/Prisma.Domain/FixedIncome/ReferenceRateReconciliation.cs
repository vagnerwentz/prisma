namespace Prisma.Domain.FixedIncome;

// O que a sincronização fez com as taxas: as que entraram (ainda sem gravar), as datas cujo valor mudou na fonte e quantas
// já estavam certas. Quem sincroniza grava `Added` e loga `Revised`.
public sealed record ReferenceRateChanges(IReadOnlyList<ReferenceRate> Added, IReadOnlyList<DateOnly> Revised, int Unchanged)
{
    public bool HasChanges => Added.Count > 0 || Revised.Count > 0;
}

// Compara as taxas que chegaram da fonte com as já gravadas (docs/renda-fixa.md, 3.2). Regras:
//  - data nova entra;
//  - data conhecida com o mesmo valor não faz nada (é o que torna a sincronização idempotente: rodar de novo, ou duas ao
//    mesmo tempo, dá o mesmo estado);
//  - data conhecida com valor diferente é revisão: o valor muda, RevisedAt anota quando, e quem chama loga. Nunca silencioso;
//  - o que está gravado e não veio no lote fica como está. A sincronização nunca apaga taxa.
public static class ReferenceRateReconciliation
{
    public static ReferenceRateChanges Apply(
        ReferenceRateSeries series, IEnumerable<ReferenceRate> existing, IReadOnlyList<DailyRate> incoming, string source, DateTime utcNow)
    {
        var byDate = new Dictionary<DateOnly, ReferenceRate>();
        foreach (var rate in existing)
            if (!byDate.TryAdd(rate.ReferenceDate, rate))
                throw new ArgumentException($"Há duas taxas gravadas em {rate.ReferenceDate:yyyy-MM-dd}.", nameof(existing));

        var seen = new HashSet<DateOnly>();
        var added = new List<ReferenceRate>();
        var revised = new List<DateOnly>();
        var unchanged = 0;
        foreach (var rate in incoming)
        {
            if (!seen.Add(rate.Date))
                throw new ArgumentException($"A fonte trouxe a data {rate.Date:yyyy-MM-dd} duas vezes.", nameof(incoming));

            if (!byDate.TryGetValue(rate.Date, out var stored))
                added.Add(ReferenceRate.From(series, rate, source, utcNow));
            else if (stored.Revise(rate, utcNow))
                revised.Add(rate.Date);
            else
                unchanged++;
        }

        return new ReferenceRateChanges(added, revised, unchanged);
    }
}
