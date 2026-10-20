using Prisma.Domain.Calendar;

namespace Prisma.Domain.FixedIncome.Prefixed;

// Rendimento bruto de um aporte pré-fixado (docs/renda-fixa.md, seção 5.5): fator = (1 + taxa)^(du / 252), com `du` os dias úteis
// do dia do aporte (inclusive) até a referência (exclusive), a mesma convenção de datas do CDI. A conta é um fator por dia útil
// ((1 + taxa)^(1/252), AnnualRate.DailyFactor), truncado em 16 casas a cada passo, e o final arredondado em 8.
//
// Não lê a série do CDI: os dias úteis vêm do calendário bancário (decisão do dono, 2026-10-18; é o quarto uso permitido do
// calendário, e mora neste namespace para a regra de arquitetura enxergar). Por isso o saldo vale até a data pedida, mesmo com o
// Banco Central fora do ar. Puro como o resto do motor: sem relógio, banco nem rede.
internal static class PrefixedYield
{
    // O laço anda dia a dia: um prazo absurdo é erro de dado, não aporte.
    public const int MaxTermYears = 50;

    public static Result<FixedIncomeBalance> Balance(Money principal, DateOnly depositDate, AnnualRate rate, DateOnly asOf)
    {
        if (asOf > depositDate.AddYears(MaxTermYears))
            return TermError;

        var daily = rate.DailyFactor();
        Int128 accumulated = FixedPoint.Scale16;
        var used = 0;
        for (var day = depositDate; day < asOf; day = day.AddDays(1))
        {
            if (!BankCalendar.IsBusinessDay(day))
                continue;

            accumulated = accumulated * daily / FixedPoint.Scale16;
            if (accumulated > FixedPoint.MaxAccumulated)
                return OutOfRange;

            used++;
        }

        var factorE8 = FixedPoint.FactorE8(accumulated);
        return new FixedIncomeBalance(principal, FixedPoint.Gross(principal, factorE8), factorE8, used, null);
    }

    public static Result<IReadOnlyList<Money?>> Series(
        Money principal, DateOnly depositDate, AnnualRate rate, IReadOnlyList<DateOnly> asOfDates)
    {
        var factors = Factors(depositDate, rate, asOfDates);
        if (!factors.IsSuccess)
            return factors.Error;

        var points = new Money?[asOfDates.Count];
        for (var i = 0; i < points.Length; i++)
            if (factors.Value[i] is { } factor)
                points[i] = FixedPoint.Gross(principal, factor);

        return points;
    }

    // O fator (8 casas) em cada data, numa passada só; nulo antes da data do aporte.
    public static Result<long?[]> Factors(DateOnly depositDate, AnnualRate rate, IReadOnlyList<DateOnly> asOfDates)
    {
        if (asOfDates.Count > 0 && asOfDates[^1] > depositDate.AddYears(MaxTermYears))
            return TermError;

        var daily = rate.DailyFactor();
        var points = new long?[asOfDates.Count];
        Int128 accumulated = FixedPoint.Scale16;
        var day = depositDate;
        for (var i = 0; i < asOfDates.Count; i++)
        {
            if (asOfDates[i] < depositDate)
                continue;

            for (; day < asOfDates[i]; day = day.AddDays(1))
            {
                if (!BankCalendar.IsBusinessDay(day))
                    continue;

                accumulated = accumulated * daily / FixedPoint.Scale16;
                if (accumulated > FixedPoint.MaxAccumulated)
                    return OutOfRange;
            }

            points[i] = FixedPoint.FactorE8(accumulated);
        }

        return points;
    }

    private static Error TermError => new(ErrorType.Validation, "O prazo do aporte está fora do limite.");

    private static Error OutOfRange => new(ErrorType.Validation, "O rendimento calculado está fora do limite.");
}
