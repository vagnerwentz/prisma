namespace Prisma.Domain.FixedIncome;

// A série de taxas diárias já carregada: datas estritamente crescentes, uma taxa por data publicada. Dia sem taxa (fim de
// semana, feriado) simplesmente não está na lista; o motor nunca preenche lacuna com zero nem com estimativa.
// Detectar dia útil que faltou é trabalho de quem sincroniza a série (R.3), não do cálculo.
public sealed class RateHistory
{
    // Ao dia, de 1 × 10⁻⁸ até menos de 1 % (3.600 % ao ano): fora disso é dado corrompido, não taxa.
    public const long MaxRateE8 = 1_000_000;

    private readonly DailyRate[] _rates;

    private RateHistory(DailyRate[] rates) => _rates = rates;

    public int Count => _rates.Length;

    public DailyRate this[int index] => _rates[index];

    public DateOnly? FirstDate => _rates.Length == 0 ? null : _rates[0].Date;

    public DateOnly? LastDate => _rates.Length == 0 ? null : _rates[^1].Date;

    public static Result<RateHistory> Create(IEnumerable<DailyRate> rates)
    {
        var list = rates.ToArray();
        for (var i = 0; i < list.Length; i++)
        {
            if (list[i].RateE8 is < 1 or >= MaxRateE8)
                return new Error(ErrorType.Validation, $"Taxa fora do limite em {list[i].Date:dd/MM/yyyy}.", "InvalidRate");
            if (i > 0 && list[i].Date <= list[i - 1].Date)
                return new Error(ErrorType.Validation, $"As taxas precisam estar em ordem de data, sem repetir ({list[i].Date:dd/MM/yyyy}).", "InvalidRate");
        }

        return new RateHistory(list);
    }

    // Posição da primeira taxa com data igual ou depois de `date`; Count se não houver.
    public int IndexOfFirstOnOrAfter(DateOnly date)
    {
        int low = 0, high = _rates.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_rates[middle].Date < date)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
