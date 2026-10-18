namespace Prisma.Domain.FixedIncome;

// Rendimento bruto de um aporte em CDB pós-fixado, a x % do CDI (docs/renda-fixa.md, seção 5.1). É a convenção da B3
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

    private const long Scale16 = 10_000_000_000_000_000;

    private const long Scale8 = 100_000_000;

    // O fator acumulado não passa de 1.000×: acima disso o histórico ou o percentual estão errados.
    private static readonly Int128 MaxAccumulated = (Int128)1_000 * Scale16;

    public static Result<FixedIncomeBalance> Balance(
        Money principal, DateOnly depositDate, IndexPercent percent, RateHistory rates, DateOnly asOf)
    {
        if (principal.Cents < 1 || principal.Cents > MaxPrincipalCents)
            return new Error(ErrorType.Validation, "O valor do aporte está fora do limite.");
        if (!percent.IsInRange)
            return new Error(ErrorType.Validation, "O percentual do CDI está fora do limite (de 0,01% a 500%).");
        if (asOf < depositDate)
            return new Error(ErrorType.Validation, "A data de referência é anterior à data do aporte.");
        if (rates.FirstDate is not { } first || depositDate < first)
            return new Error(
                ErrorType.Validation, "Não há taxa do CDI para a data do aporte.", "RatesUnavailable");

        Int128 accumulated = Scale16;
        var used = 0;
        DateOnly? through = null;
        for (var i = rates.IndexOfFirstOnOrAfter(depositDate); i < rates.Count && rates[i].Date < asOf; i++)
        {
            // p/100 × TDI tem 14 casas: exato em 16, sem arredondar ("16 casas sem arredondamento" da B3).
            var daily = Scale16 + (Int128)rates[i].RateE8 * percent.TenThousandths * 100;
            accumulated = accumulated * daily / Scale16;
            if (accumulated > MaxAccumulated)
                return new Error(ErrorType.Validation, "O rendimento calculado está fora do limite.");

            used++;
            through = rates[i].Date;
        }

        var factorE8 = (long)((accumulated + Scale8 / 2) / (Scale16 / Scale8));
        var gross = (long)(((Int128)principal.Cents * factorE8 + Scale8 / 2) / Scale8);

        return new FixedIncomeBalance(principal, new Money(gross), factorE8, used, through);
    }

    // O saldo bruto do aporte em várias datas de uma vez, para o gráfico da evolução (docs/renda-fixa.md, 6.2). Uma passada só
    // pelas taxas: o fator vai acumulando e cada data lê o saldo daquele ponto, em vez de recalcular do zero. O contrato é um só: o
    // valor de cada data é exatamente o que Balance dá para ela. Antes da data do aporte não há saldo (nulo).
    //
    // `asOfDates` em ordem estritamente crescente. A lista devolvida tem uma posição por data.
    public static Result<IReadOnlyList<Money?>> Series(
        Money principal, DateOnly depositDate, IndexPercent percent, RateHistory rates, IReadOnlyList<DateOnly> asOfDates)
    {
        for (var i = 1; i < asOfDates.Count; i++)
            if (asOfDates[i] <= asOfDates[i - 1])
                throw new ArgumentException("As datas precisam estar em ordem estritamente crescente.", nameof(asOfDates));

        if (principal.Cents < 1 || principal.Cents > MaxPrincipalCents)
            return new Error(ErrorType.Validation, "O valor do aporte está fora do limite.");
        if (!percent.IsInRange)
            return new Error(ErrorType.Validation, "O percentual do CDI está fora do limite (de 0,01% a 500%).");
        if (rates.FirstDate is not { } first || depositDate < first)
            return new Error(
                ErrorType.Validation, "Não há taxa do CDI para a data do aporte.", "RatesUnavailable");

        var points = new Money?[asOfDates.Count];
        Int128 accumulated = Scale16;
        var next = rates.IndexOfFirstOnOrAfter(depositDate);
        for (var i = 0; i < asOfDates.Count; i++)
        {
            if (asOfDates[i] < depositDate)
                continue;

            for (; next < rates.Count && rates[next].Date < asOfDates[i]; next++)
            {
                var daily = Scale16 + (Int128)rates[next].RateE8 * percent.TenThousandths * 100;
                accumulated = accumulated * daily / Scale16;
                if (accumulated > MaxAccumulated)
                    return new Error(ErrorType.Validation, "O rendimento calculado está fora do limite.");
            }

            var factorE8 = (long)((accumulated + Scale8 / 2) / (Scale16 / Scale8));
            points[i] = new Money((long)(((Int128)principal.Cents * factorE8 + Scale8 / 2) / Scale8));
        }

        return points;
    }
}
