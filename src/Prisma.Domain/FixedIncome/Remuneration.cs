namespace Prisma.Domain.FixedIncome;

// Taxa fixa ao ano, em décimos de milésimo de ponto percentual (a mesma escala do IndexPercent): 14 % = 140.000. Serve ao
// CDI+ (a taxa somada ao CDI) e ao pré-fixado (a taxa inteira). Base de 252 dias úteis (docs/renda-fixa.md, seção 5.5).
//
// Faixa aceita: de 0,01 % a 100 % ao ano.
public readonly record struct AnnualRate(long TenThousandths)
{
    public const long Min = 100;

    public const long Max = 1_000_000;

    public const int BusinessDaysPerYear = 252;

    public bool IsInRange => TenThousandths is >= Min and <= Max;

    // O fator de um dia útil, (1 + taxa)^(1/252), em ponto fixo de 16 casas: a maior raiz 252 cuja potência (calculada como o
    // motor a calcula, passo a passo, truncando em 16 casas) não passa de 1 + taxa. Tudo em inteiro, sem double.
    internal Int128 DailyFactor()
    {
        const long scale = FixedPoint.Scale16;
        var target = scale + (Int128)TenThousandths * 10_000_000_000;

        // (1 + x)^252 ≥ 1 + 252x: a raiz nunca passa de 1 + taxa/252, e a busca fica curta (e sem estouro).
        Int128 low = scale, high = scale + (Int128)TenThousandths * 10_000_000_000 / BusinessDaysPerYear + 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Power(middle) <= target)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    private static Int128 Power(Int128 factor)
    {
        Int128 result = FixedPoint.Scale16;
        for (var i = 0; i < BusinessDaysPerYear; i++)
            result = result * factor / FixedPoint.Scale16;
        return result;
    }
}

// Como o aporte rende (docs/renda-fixa.md, seção 5.5): X % do CDI, CDI + taxa fixa ao ano, ou taxa fixa ao ano (pré-fixado).
// Um valor só, em E4: o percentual do CDI ou a taxa ao ano, conforme o tipo.
public readonly record struct Remuneration
{
    private Remuneration(FixedIncomeIndexKind kind, long valueE4)
    {
        Kind = kind;
        ValueE4 = valueE4;
    }

    // O índice do aporte: Cdi (X % do CDI), CdiPlus (CDI + taxa ao ano) ou Prefixed (taxa ao ano).
    public FixedIncomeIndexKind Kind { get; }

    public long ValueE4 { get; }

    public static Remuneration PercentOfCdi(IndexPercent percent) => new(FixedIncomeIndexKind.Cdi, percent.TenThousandths);

    public static Remuneration CdiPlus(AnnualRate rate) => new(FixedIncomeIndexKind.CdiPlus, rate.TenThousandths);

    public static Remuneration Prefixed(AnnualRate rate) => new(FixedIncomeIndexKind.Prefixed, rate.TenThousandths);

    // Do que o aporte guarda: o índice e a taxa (o percentual do CDI, ou a taxa ao ano, conforme o índice), ambos em E4.
    public static Remuneration Of(FixedIncomeIndexKind kind, long rateE4) => new(kind, rateE4);

    public bool IsInRange => Kind switch
    {
        FixedIncomeIndexKind.Cdi => new IndexPercent(ValueE4).IsInRange,
        FixedIncomeIndexKind.CdiPlus or FixedIncomeIndexKind.Prefixed => new AnnualRate(ValueE4).IsInRange,
        _ => false,
    };

    // Precisa das taxas diárias do CDI? O pré-fixado não: conta só dias úteis.
    public bool UsesCdi => Kind != FixedIncomeIndexKind.Prefixed;

    internal string RangeMessage => Kind == FixedIncomeIndexKind.Cdi
        ? "O percentual do CDI está fora do limite (de 0,01% a 500%)."
        : "A taxa ao ano está fora do limite (de 0,01% a 100%).";
}
