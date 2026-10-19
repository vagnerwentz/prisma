using System.Numerics;
using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// Rendimento do CDB pré-fixado (docs/renda-fixa.md, seção 5.5): (1 + taxa)^(du / 252), du os dias úteis do dia do aporte
// (inclusive) até a referência (exclusive). Os valores esperados saem de fora do código: Python com Decimal de 60 dígitos e uma
// lista de feriados escrita à parte (o BankCalendar não foi consultado), e a calculadora que o dono usou (R$ 5.000 a 14 % a.a.).
public sealed class PrefixedYieldTests
{
    private static readonly RateHistory NoRates = RateHistory.Create([]).Value;

    private static FixedIncomeBalance Balance(long cents, DateOnly deposit, DateOnly asOf, long rateE4) =>
        FixedIncomeEngine.Balance(
            new Money(cents), deposit, Remuneration.Prefixed(new AnnualRate(rateE4)), NoRates, asOf).Value;

    // ---- Vetores independentes ----

    // Aporte, datas, taxa ao ano, dias úteis contados à mão (com Carnaval, Sexta-feira Santa, Corpus Christi e feriados
    // fixos), fator de 8 casas e saldo. O primeiro, de 01/10/2026 a 01/10/2027, tem 250 dias úteis e não 252: um ano de
    // calendário não é um ano de 252 dias.
    [Theory]
    [InlineData(500000, 2026, 10, 1, 2027, 10, 1, 140000, 250, 113881512, 569408)]
    [InlineData(500000, 2026, 10, 1, 2028, 10, 2, 140000, 501, 129757439, 648787)]
    [InlineData(1000000, 2026, 10, 16, 2026, 11, 16, 120000, 20, 100903491, 1009035)]
    [InlineData(250000, 2026, 10, 17, 2027, 3, 1, 105000, 89, 103589194, 258973)]
    [InlineData(10000000, 2026, 1, 2, 2026, 12, 31, 137500, 248, 113517623, 11351762)]
    [InlineData(123456, 2026, 5, 29, 2026, 6, 8, 80000, 5, 100152817, 123645)]
    [InlineData(100, 2026, 10, 1, 2026, 10, 2, 1000000, 1, 100275437, 100)]
    public void Matches_the_independent_calculation(
        long cents, int y1, int m1, int d1, int y2, int m2, int d2, long rateE4, int businessDays, long factorE8, long expected)
    {
        var result = Balance(cents, new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2), rateE4);

        result.RatesUsed.ShouldBe(businessDays);
        result.FactorE8.ShouldBe(factorE8);
        result.GrossBalance.ShouldBe(new Money(expected));
    }

    // R$ 5.000 a 14 % a.a. por 252 dias úteis exatos: R$ 5.700,00 (a conta que o dono conferiu numa calculadora). De 01/10/2026
    // a 05/10/2027 (exclusive) há 252 dias úteis.
    [Fact]
    public void Five_thousand_at_14_percent_for_252_business_days_is_5700()
    {
        var result = Balance(500_000, new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 5), 140_000);

        result.RatesUsed.ShouldBe(252);
        result.FactorE8.ShouldBe(114_000_000);
        result.GrossBalance.ShouldBe(new Money(570_000));
        result.GrossYield.ShouldBe(new Money(70_000));
    }

    // Dois anos, 504 dias úteis: 1,14² = 1,2996 → R$ 6.498,00.
    [Fact]
    public void Five_thousand_at_14_percent_for_504_business_days_is_6498()
    {
        // De 01/10/2026 a 05/10/2028 (exclusive): 504 dias úteis, contados à parte no Python.
        var result = Balance(500_000, new DateOnly(2026, 10, 1), new DateOnly(2028, 10, 5), 140_000);

        result.RatesUsed.ShouldBe(504);
        result.FactorE8.ShouldBe(129_960_000);
        result.GrossBalance.ShouldBe(new Money(649_800));
    }

    // ---- Convenção de datas ----

    [Fact]
    public void The_deposit_day_counts_and_the_reference_day_does_not()
    {
        // Segunda-feira 05/10/2026: um dia útil entre a segunda (inclusive) e a terça (exclusive).
        var one = Balance(1_000_000, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 120_000);
        var none = Balance(1_000_000, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), 120_000);

        one.RatesUsed.ShouldBe(1);
        none.RatesUsed.ShouldBe(0);
        none.GrossBalance.ShouldBe(new Money(1_000_000));
        none.FactorE8.ShouldBe(100_000_000);
    }

    [Fact]
    public void A_deposit_on_a_weekend_starts_rendering_on_the_next_business_day()
    {
        // Sábado 03/10/2026 a segunda 05/10 (exclusive): só sábado e domingo, nenhum dia útil.
        var weekend = Balance(1_000_000, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5), 120_000);
        // Até terça 06/10 (exclusive): a segunda entra.
        var monday = Balance(1_000_000, new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 6), 120_000);

        weekend.RatesUsed.ShouldBe(0);
        weekend.GrossBalance.ShouldBe(new Money(1_000_000));
        monday.RatesUsed.ShouldBe(1);
        monday.GrossBalance.Cents.ShouldBeGreaterThan(1_000_000);
    }

    [Fact]
    public void Holidays_do_not_render_and_the_balance_does_not_depend_on_the_cdi_history()
    {
        // De sexta 30/10 a terça 03/11 (exclusive): a sexta rende; sábado, domingo e segunda 02/11 (Finados) não.
        var result = Balance(1_000_000, new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 3), 120_000);

        result.RatesUsed.ShouldBe(1);
        result.RatesThrough.ShouldBeNull();
    }

    // ---- Erros ----

    [Fact]
    public void A_rate_outside_the_range_is_refused()
    {
        foreach (var rate in new long[] { 0, 99, 1_000_001 })
        {
            var result = FixedIncomeEngine.Balance(
                new Money(100_000), new DateOnly(2026, 10, 1), Remuneration.Prefixed(new AnnualRate(rate)), NoRates, new DateOnly(2026, 10, 16));

            result.IsSuccess.ShouldBeFalse($"taxa {rate}");
            result.Error.Message.ShouldContain("0,01% a 100%");
        }
    }

    [Fact]
    public void The_boundaries_of_the_range_are_accepted()
    {
        Balance(100_000, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), AnnualRate.Min).GrossBalance.Cents.ShouldBeGreaterThanOrEqualTo(100_000);
        Balance(100_000, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), AnnualRate.Max).GrossBalance.Cents.ShouldBeGreaterThan(100_000);
    }

    [Fact]
    public void A_reference_before_the_deposit_and_an_absurd_term_are_refused()
    {
        var before = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 10, 10), Remuneration.Prefixed(new AnnualRate(120_000)), NoRates, new DateOnly(2026, 10, 9));
        var far = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 10, 10), Remuneration.Prefixed(new AnnualRate(120_000)), NoRates, new DateOnly(2090, 1, 1));

        before.IsSuccess.ShouldBeFalse();
        far.IsSuccess.ShouldBeFalse();
        far.Error.Message.ShouldContain("prazo");
    }

    [Fact]
    public void A_principal_outside_the_limit_is_refused()
    {
        foreach (var cents in new[] { 0L, -1L, FixedIncomeEngine.MaxPrincipalCents + 1 })
            FixedIncomeEngine.Balance(
                new Money(cents), new DateOnly(2026, 10, 1), Remuneration.Prefixed(new AnnualRate(120_000)), NoRates, new DateOnly(2026, 10, 16))
                .IsSuccess.ShouldBeFalse($"valor {cents}");
    }

    // ---- Série ----

    // O contrato é o de sempre: o ponto de cada data é o saldo que Balance dá para ela; antes do aporte não há saldo.
    [Theory]
    [InlineData(609_731, 2026, 10, 1, 140_000)]
    [InlineData(100, 2026, 11, 20, 1_000_000)]
    [InlineData(1_000_000, 2026, 10, 3, 120_000)]
    public void Each_series_point_is_exactly_the_balance_for_that_day(long cents, int year, int month, int day, long rateE4)
    {
        var deposit = new DateOnly(year, month, day);
        var from = new DateOnly(2026, 9, 25);
        var dates = Enumerable.Range(0, 400).Select(from.AddDays).ToList();
        var remuneration = Remuneration.Prefixed(new AnnualRate(rateE4));

        var series = FixedIncomeEngine.Series(new Money(cents), deposit, remuneration, NoRates, dates).Value;

        series.Count.ShouldBe(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] < deposit)
                series[i].ShouldBeNull($"antes do aporte, em {dates[i]}");
            else
                series[i].ShouldBe(
                    FixedIncomeEngine.Balance(new Money(cents), deposit, remuneration, NoRates, dates[i]).Value.GrossBalance,
                    $"em {dates[i]}");
        }
    }

    // ---- Propriedades ----

    // Oráculo exato, sem ponto fixo: x = ((1e6 + T) / 1e6)^(n / 252), pela raiz 252 inteira de 10^(252 × 14) × ((1e6 + T)/1e6)^n,
    // arredondado em 8 casas. Outro algoritmo, em BigInteger.
    private static long OracleFactorE8(long rateE4, int businessDays)
    {
        const int digits = 14;
        var scaled = BigInteger.Pow(10, 252 * digits) * BigInteger.Pow(1_000_000 + rateE4, businessDays)
            / BigInteger.Pow(1_000_000, businessDays);

        BigInteger low = 0, high = BigInteger.Pow(10, digits + 4);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (BigInteger.Pow(middle, 252) <= scaled)
                low = middle;
            else
                high = middle - 1;
        }

        var drop = BigInteger.Pow(10, digits - 8);
        return (long)((low + drop / 2) / drop);
    }

    [Fact]
    public void The_factor_matches_the_exact_root_for_any_rate_and_term() =>
        Gen.Select(Gen.Long[AnnualRate.Min, AnnualRate.Max], Gen.Int[0, 2000], Gen.Int[0, 800])
            .Sample((rateE4, depositSeed, span) =>
            {
                var deposit = new DateOnly(2026, 1, 2).AddDays(depositSeed);
                var result = FixedIncomeEngine.Balance(
                    new Money(100_000), deposit, Remuneration.Prefixed(new AnnualRate(rateE4)), NoRates, deposit.AddDays(span));

                // Acima de 1.000× o motor recusa, e a taxa de 100 % por 800 dias úteis não chega perto disso.
                return result.IsSuccess
                    && Math.Abs(result.Value.FactorE8 - OracleFactorE8(rateE4, result.Value.RatesUsed)) <= 1;
            }, iter: 300);

    [Fact]
    public void The_balance_never_falls_with_time_and_never_falls_with_a_higher_rate() =>
        Gen.Select(Gen.Long[AnnualRate.Min, AnnualRate.Max - 1], Gen.Int[0, 2000], Gen.Int[0, 800], Gen.Int[0, 100], Gen.Long[1, 100_000_000_00])
            .Sample((rateE4, depositSeed, span, extra, cents) =>
            {
                var deposit = new DateOnly(2026, 1, 2).AddDays(depositSeed);
                Money Of(long rate, int days) => FixedIncomeEngine.Balance(
                    new Money(cents), deposit, Remuneration.Prefixed(new AnnualRate(rate)), NoRates, deposit.AddDays(days)).Value.GrossBalance;

                return Of(rateE4, span + extra).Cents >= Of(rateE4, span).Cents
                    && Of(rateE4 + 1, span).Cents >= Of(rateE4, span).Cents
                    && Of(rateE4, span).Cents >= cents;
            }, iter: 300);
}
