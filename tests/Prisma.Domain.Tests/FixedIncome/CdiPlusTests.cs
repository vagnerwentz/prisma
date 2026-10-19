using System.Numerics;
using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// CDB a CDI + taxa fixa (docs/renda-fixa.md, seção 5.5): o fator do CDI a 100 % vezes (1 + s)^(n/252), n as taxas usadas. Os valores
// esperados saem de fora do código: Python com Decimal de 60 dígitos sobre as taxas reais do BCB (Cdi2026), com o CDI truncado em 16
// casas por passo e o fator da taxa fixa exato.
public sealed class CdiPlusTests
{
    private static Remuneration CdiPlus(long rateE4) => Remuneration.CdiPlus(new AnnualRate(rateE4));

    private static Result<FixedIncomeBalance> Balance(long cents, DateOnly deposit, DateOnly asOf, Remuneration remuneration, RateHistory? rates = null) =>
        FixedIncomeEngine.Balance(new Money(cents), deposit, remuneration, rates ?? Cdi2026.History, asOf);

    // Aporte, datas, taxa fixa ao ano, taxas usadas, fator de 8 casas e saldo.
    [Theory]
    [InlineData(609731, 2026, 7, 21, 2026, 10, 16, 20000, 61, 103689235, 632225)]
    [InlineData(1000000, 2026, 8, 10, 2026, 10, 15, 60000, 46, 103482078, 1034821)]
    [InlineData(500000, 2026, 9, 1, 2026, 9, 30, 10000, 20, 101110177, 505551)]
    [InlineData(100000, 2026, 7, 21, 2026, 7, 22, 1000000, 1, 100328113, 100328)]
    public void Matches_the_independent_calculation(
        long cents, int y1, int m1, int d1, int y2, int m2, int d2, long rateE4, int used, long factorE8, long expected)
    {
        var result = Balance(cents, new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2), CdiPlus(rateE4));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RatesUsed.ShouldBe(used);
        result.Value.FactorE8.ShouldBe(factorE8);
        result.Value.GrossBalance.ShouldBe(new Money(expected));
    }

    // A parte do CDI é a de 100 %: sem taxa fixa relevante, o CDI+ fica logo acima do CDI puro e abaixo de 101 % dele.
    [Fact]
    public void A_tiny_spread_lands_just_above_100_percent_of_the_cdi()
    {
        var deposit = new DateOnly(2026, 7, 21);
        var asOf = new DateOnly(2026, 10, 16);
        var cdi = Balance(10_000_000, deposit, asOf, Remuneration.PercentOfCdi(new IndexPercent(1_000_000))).Value;
        var plus = Balance(10_000_000, deposit, asOf, CdiPlus(AnnualRate.Min)).Value;

        plus.GrossBalance.Cents.ShouldBeGreaterThanOrEqualTo(cdi.GrossBalance.Cents);
        plus.GrossBalance.Cents.ShouldBeLessThanOrEqualTo(cdi.GrossBalance.Cents + 1_000);
        plus.RatesUsed.ShouldBe(cdi.RatesUsed);
        plus.RatesThrough.ShouldBe(cdi.RatesThrough);
    }

    // O CDI+ não é "% do CDI": os 10 % extras de 110 % do CDI valem 10 % da taxa do CDI, e a taxa fixa vale o que foi contratado.
    // Com o CDI de 2026 (~14,1 % a.a.) os 10 % extras são ~1,4 % ao ano: CDI + 2 % rende mais que 110 % do CDI, e CDI + 1 % rende
    // menos. O cruzamento depende do CDI, por isso os dois produtos não se confundem.
    [Fact]
    public void Cdi_plus_crosses_110_percent_of_the_cdi_where_ten_percent_of_the_cdi_equals_the_spread()
    {
        var deposit = new DateOnly(2026, 7, 21);
        var asOf = new DateOnly(2026, 10, 16);
        var percent = Balance(10_000_000, deposit, asOf, Remuneration.PercentOfCdi(new IndexPercent(1_100_000))).Value.GrossBalance;

        Balance(10_000_000, deposit, asOf, CdiPlus(20_000)).Value.GrossBalance.Cents.ShouldBeGreaterThan(percent.Cents);
        Balance(10_000_000, deposit, asOf, CdiPlus(10_000)).Value.GrossBalance.Cents.ShouldBeLessThan(percent.Cents);
    }

    [Fact]
    public void A_deposit_before_the_first_rate_is_refused_like_the_cdi()
    {
        var result = Balance(100_000, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 1), CdiPlus(20_000));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("RatesUnavailable");
    }

    [Fact]
    public void A_rate_outside_the_range_is_refused()
    {
        foreach (var rate in new long[] { 0, 99, 1_000_001 })
        {
            var result = Balance(100_000, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), CdiPlus(rate));

            result.IsSuccess.ShouldBeFalse($"taxa {rate}");
            result.Error.Message.ShouldContain("0,01% a 100%");
        }
    }

    [Fact]
    public void Without_any_day_in_the_window_the_balance_is_the_principal()
    {
        var result = Balance(123_456, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), CdiPlus(20_000)).Value;

        result.GrossBalance.ShouldBe(new Money(123_456));
        result.FactorE8.ShouldBe(100_000_000);
        result.RatesUsed.ShouldBe(0);
    }

    // O ponto de cada data é o saldo que Balance dá para ela, como no CDI.
    [Theory]
    [InlineData(609_731, 2026, 7, 21, 20_000)]
    [InlineData(1_047_408, 2026, 7, 22, 150_000)]
    [InlineData(100, 2026, 10, 15, 1_000_000)]
    public void Each_series_point_is_exactly_the_balance_for_that_day(long cents, int year, int month, int day, long rateE4)
    {
        var deposit = new DateOnly(year, month, day);
        var dates = Enumerable.Range(0, 120).Select(new DateOnly(2026, 7, 1).AddDays).ToList();
        var remuneration = CdiPlus(rateE4);

        var series = FixedIncomeEngine.Series(new Money(cents), deposit, remuneration, Cdi2026.History, dates).Value;

        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] < deposit)
                series[i].ShouldBeNull($"antes do aporte, em {dates[i]}");
            else
                series[i].ShouldBe(Balance(cents, deposit, dates[i], remuneration).Value.GrossBalance, $"em {dates[i]}");
        }
    }

    // ---- Propriedades ----

    private static readonly DateOnly Start = new(2026, 1, 2);

    private static RateHistory HistoryOf(List<long> rates) =>
        RateHistory.Create(rates.Select((r, i) => new DailyRate(Start.AddDays(i), r))).Value;

    // Oráculo: CDI a 100 % truncado em 16 casas por passo, taxa fixa pela raiz 252 inteira exata (14 dígitos), produto em 8 casas.
    private static long OracleFactorE8(long spreadE4, IReadOnlyList<long> ratesE8)
    {
        BigInteger scale16 = BigInteger.Pow(10, 16);
        var cdi = scale16;
        foreach (var rate in ratesE8)
            cdi = cdi * (scale16 + new BigInteger(rate) * 100_000_000) / scale16;

        const int digits = 14;
        var n = ratesE8.Count;
        var scaled = BigInteger.Pow(10, 252 * digits) * BigInteger.Pow(1_000_000 + spreadE4, n) / BigInteger.Pow(1_000_000, n);
        BigInteger low = 0, high = BigInteger.Pow(10, digits + 4);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (BigInteger.Pow(middle, 252) <= scaled)
                low = middle;
            else
                high = middle - 1;
        }

        // cdi tem 16 casas e `low` 14: o produto tem 30; até 8 casas tira 22.
        var drop = BigInteger.Pow(10, 22);
        return (long)((cdi * low + drop / 2) / drop);
    }

    [Fact]
    public void The_factor_matches_the_arbitrary_precision_oracle() =>
        Gen.Select(Gen.Long[1, 150_000].List[1, 300], Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Long[AnnualRate.Min, AnnualRate.Max])
            .Sample((rates, depositSeed, spanSeed, spreadE4) =>
            {
                var deposit = Start.AddDays(depositSeed % rates.Count);
                var asOf = deposit.AddDays(spanSeed % (rates.Count + 3));
                var result = FixedIncomeEngine.Balance(new Money(100_000), deposit, CdiPlus(spreadE4), HistoryOf(rates), asOf);

                var used = rates.Where((_, i) => Start.AddDays(i) >= deposit && Start.AddDays(i) < asOf).ToList();
                return result.IsSuccess && Math.Abs(result.Value.FactorE8 - OracleFactorE8(spreadE4, used)) <= 1;
            }, iter: 300);
}
