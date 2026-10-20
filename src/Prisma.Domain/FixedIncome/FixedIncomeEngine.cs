namespace Prisma.Domain.FixedIncome;

// Rendimento bruto de um aporte em CDB (docs/renda-fixa.md, seções 5.1 e 5.5). Pós-fixado a x % do CDI: É a convenção da B3
// ("Metodologia de Cálculo Acumulado de DI"), a do mercado, conferida com extratos do banco (diferença máxima de R$ 0,01):
//
//   f_k = 1 + (p / 100) × TDI_k        TDI_k é a taxa diária da série, p o percentual contratado
//   C   = ∏ f_k                        a cada passo, trunca em 16 casas; o C final é arredondado em 8 casas
//   saldo = round(principal × C)       em centavos, meio centavo para cima
//
// Entram as taxas com data de `depositDate` (inclusive) até `asOf` (exclusive): a taxa do dia da aplicação conta e a do dia
// da referência não. Sem taxa não há rendimento: aporte antes da primeira taxa conhecida é recusado, nunca tratado como zero.
//
// Tudo em inteiro: o fator é ponto fixo em Int128 com 16 casas (CLAUDE.md, regra 1; docs/renda-fixa.md, D1). O motor é puro:
// não lê relógio, banco nem rede, e a mesma entrada dá sempre a mesma saída.
public static class FixedIncomeEngine
{
    // Teto do aporte, R$ 1 bilhão, para que principal × fator caiba com folga em Int128.
    public const long MaxPrincipalCents = 100_000_000_000;

    // O percentual de 100 % do CDI, para o CDI+ (a taxa fixa entra como um fator à parte).
    private const long Cdi100 = 1_000_000;

    public static Result<FixedIncomeBalance> Balance(
        Money principal, DateOnly depositDate, IndexPercent percent, RateHistory rates, DateOnly asOf) =>
        Balance(principal, depositDate, Remuneration.PercentOfCdi(percent), rates, asOf);

    // CDI+ (docs/renda-fixa.md, 5.5): o fator do CDI a 100 % vezes o da taxa fixa, (1 + s)^(n/252), com n as taxas usadas.
    // Os dois acumulam em 16 casas, e o produto é arredondado uma vez só, em 8. Pré-fixado: não lê `rates` (PrefixedYield).
    public static Result<FixedIncomeBalance> Balance(
        Money principal, DateOnly depositDate, Remuneration remuneration, RateHistory rates, DateOnly asOf)
    {
        if (principal.Cents < 1 || principal.Cents > MaxPrincipalCents)
            return new Error(ErrorType.Validation, "O valor do aporte está fora do limite.");
        if (!remuneration.IsInRange)
            return new Error(ErrorType.Validation, remuneration.RangeMessage);
        if (asOf < depositDate)
            return new Error(ErrorType.Validation, "A data de referência é anterior à data do aporte.");
        if (!remuneration.UsesCdi)
            return Prefixed.PrefixedYield.Balance(principal, depositDate, new AnnualRate(remuneration.ValueE4), asOf);
        if (rates.FirstDate is not { } first || depositDate < first)
            return new Error(
                ErrorType.Validation, "Não há taxa do CDI para a data do aporte.", "RatesUnavailable");

        var (percent, spread) = Parts(remuneration);
        Int128 cdi = FixedPoint.Scale16, fixedPart = FixedPoint.Scale16;
        var used = 0;
        DateOnly? through = null;
        for (var i = rates.IndexOfFirstOnOrAfter(depositDate); i < rates.Count && rates[i].Date < asOf; i++)
        {
            Step(ref cdi, ref fixedPart, rates[i], percent, spread);
            if (Product(cdi, fixedPart) > FixedPoint.MaxAccumulated)
                return new Error(ErrorType.Validation, "O rendimento calculado está fora do limite.");

            used++;
            through = rates[i].Date;
        }

        var factorE8 = FixedPoint.FactorE8(Product(cdi, fixedPart));
        return new FixedIncomeBalance(principal, FixedPoint.Gross(principal, factorE8), factorE8, used, through);
    }

    // Com resgates: o fator é o do aporte inteiro (a taxa não depende do quanto sobrou) e o principal é o que sobra na data.
    // Resgate com data igual ou anterior a `asOf` já conta. Sem resgates, é exatamente o `Balance` de sempre.
    public static Result<FixedIncomeBalance> Balance(
        Money principal, DateOnly depositDate, Remuneration remuneration, RateHistory rates, DateOnly asOf,
        IReadOnlyList<RedemptionStep> redemptions)
    {
        var whole = Balance(principal, depositDate, remuneration, rates, asOf);
        if (!whole.IsSuccess || redemptions.Count == 0)
            return whole;
        if (StepsError(principal, depositDate, redemptions) is { } error)
            return error;

        var remaining = new Money(Remaining(principal, redemptions, asOf));
        return whole.Value with { Principal = remaining, GrossBalance = FixedPoint.Gross(remaining, whole.Value.FactorE8) };
    }

    public static Result<IReadOnlyList<Money?>> Series(
        Money principal, DateOnly depositDate, IndexPercent percent, RateHistory rates, IReadOnlyList<DateOnly> asOfDates) =>
        Series(principal, depositDate, Remuneration.PercentOfCdi(percent), rates, asOfDates);

    // O saldo bruto do aporte em várias datas de uma vez, para o gráfico da evolução (docs/renda-fixa.md, 6.2). Uma passada só
    // pelas taxas: o fator vai acumulando e cada data lê o saldo daquele ponto, em vez de recalcular do zero. O contrato é um só: o
    // valor de cada data é exatamente o que Balance dá para ela. Antes da data do aporte não há saldo (nulo).
    //
    // `asOfDates` em ordem estritamente crescente. A lista devolvida tem uma posição por data.
    public static Result<IReadOnlyList<Money?>> Series(
        Money principal, DateOnly depositDate, Remuneration remuneration, RateHistory rates, IReadOnlyList<DateOnly> asOfDates) =>
        Series(principal, depositDate, remuneration, rates, asOfDates, []);

    // Com resgates (docs/renda-fixa-resgate.md, 3): o principal vale por trechos, e o resgate com data igual ou anterior à da
    // referência já conta (o do dia sai do saldo do dia). O fator é o mesmo de sempre; só o principal que ele multiplica muda.
    public static Result<IReadOnlyList<Money?>> Series(
        Money principal, DateOnly depositDate, Remuneration remuneration, RateHistory rates, IReadOnlyList<DateOnly> asOfDates,
        IReadOnlyList<RedemptionStep> redemptions)
    {
        for (var i = 1; i < asOfDates.Count; i++)
            if (asOfDates[i] <= asOfDates[i - 1])
                throw new ArgumentException("As datas precisam estar em ordem estritamente crescente.", nameof(asOfDates));

        if (principal.Cents < 1 || principal.Cents > MaxPrincipalCents)
            return new Error(ErrorType.Validation, "O valor do aporte está fora do limite.");
        if (StepsError(principal, depositDate, redemptions) is { } stepsError)
            return stepsError;
        if (!remuneration.IsInRange)
            return new Error(ErrorType.Validation, remuneration.RangeMessage);

        var factors = Factors(depositDate, remuneration, rates, asOfDates);
        if (!factors.IsSuccess)
            return factors.Error;

        var points = new Money?[asOfDates.Count];
        for (var i = 0; i < points.Length; i++)
            if (factors.Value[i] is { } factor)
                points[i] = FixedPoint.Gross(new Money(Remaining(principal, redemptions, asOfDates[i])), factor);

        return points;
    }

    // O fator acumulado (8 casas) em cada data, numa passada só; nulo antes da data do aporte.
    private static Result<long?[]> Factors(
        DateOnly depositDate, Remuneration remuneration, RateHistory rates, IReadOnlyList<DateOnly> asOfDates)
    {
        if (!remuneration.UsesCdi)
            return Prefixed.PrefixedYield.Factors(depositDate, new AnnualRate(remuneration.ValueE4), asOfDates);
        if (rates.FirstDate is not { } first || depositDate < first)
            return new Error(
                ErrorType.Validation, "Não há taxa do CDI para a data do aporte.", "RatesUnavailable");

        var (percent, spread) = Parts(remuneration);
        var points = new long?[asOfDates.Count];
        Int128 cdi = FixedPoint.Scale16, fixedPart = FixedPoint.Scale16;
        var next = rates.IndexOfFirstOnOrAfter(depositDate);
        for (var i = 0; i < asOfDates.Count; i++)
        {
            if (asOfDates[i] < depositDate)
                continue;

            for (; next < rates.Count && rates[next].Date < asOfDates[i]; next++)
            {
                Step(ref cdi, ref fixedPart, rates[next], percent, spread);
                if (Product(cdi, fixedPart) > FixedPoint.MaxAccumulated)
                    return new Error(ErrorType.Validation, "O rendimento calculado está fora do limite.");
            }

            points[i] = FixedPoint.FactorE8(Product(cdi, fixedPart));
        }

        return points;
    }

    // O principal que sobra numa data: o aporte menos o que os resgates até ela (inclusive) já tiraram.
    internal static long Remaining(Money principal, IReadOnlyList<RedemptionStep> redemptions, DateOnly asOf)
    {
        var remaining = principal.Cents;
        foreach (var step in redemptions)
            if (step.Date <= asOf)
                remaining -= step.Principal.Cents;

        return remaining;
    }

    // Os resgates precisam caber no aporte: nenhum antes da data dele, nenhum de principal negativo (zero vale: resgate só de rendimento,
    // quando o fator é alto e o principal pequeno), e a soma nunca passa do principal.
    private static Error? StepsError(Money principal, DateOnly depositDate, IReadOnlyList<RedemptionStep> redemptions)
    {
        long total = 0;
        foreach (var step in redemptions)
        {
            if (step.Date < depositDate || step.Principal.Cents < 0)
                return new Error(ErrorType.Validation, "Os resgates do aporte são inválidos.");

            total += step.Principal.Cents;
        }

        return total > principal.Cents ? new Error(ErrorType.Validation, "Os resgates do aporte passam do valor aportado.") : null;
    }

    // % do CDI: o percentual contratado e nenhum fator fixo (1,0). CDI+: 100 % do CDI e o fator diário da taxa.
    private static (long Percent, Int128 Spread) Parts(Remuneration remuneration) =>
        remuneration.Kind == FixedIncomeIndexKind.CdiPlus
            ? (Cdi100, new AnnualRate(remuneration.ValueE4).DailyFactor())
            : (remuneration.ValueE4, FixedPoint.Scale16);

    private static void Step(ref Int128 cdi, ref Int128 fixedPart, DailyRate rate, long percent, Int128 spread)
    {
        // p/100 × TDI tem 14 casas: exato em 16, sem arredondar ("16 casas sem arredondamento" da B3).
        var daily = FixedPoint.Scale16 + (Int128)rate.RateE8 * percent * 100;
        cdi = cdi * daily / FixedPoint.Scale16;
        fixedPart = fixedPart * spread / FixedPoint.Scale16;
    }

    private static Int128 Product(Int128 cdi, Int128 fixedPart) => cdi * fixedPart / FixedPoint.Scale16;
}
