using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// A série do saldo de um aporte ao longo do tempo, para o gráfico (docs/renda-fixa.md, 6.2): uma passada só pelas taxas, sem
// recalcular do zero a cada data. O contrato é um só: o ponto de uma data é EXATAMENTE o saldo que o motor dá para aquela data.
public sealed class FixedIncomeSeriesTests
{
    private static readonly IndexPercent Cdi100 = new(1_000_000);
    private static readonly DateOnly Start = new(2026, 7, 21);

    private static IReadOnlyList<DateOnly> EveryDay(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    private static IReadOnlyList<Money?> Series(
        long cents, DateOnly deposit, IndexPercent percent, IReadOnlyList<DateOnly> dates) =>
        FixedIncomeEngine.Series(new Money(cents), deposit, percent, Cdi2026.History, dates).Value;

    // O ponto de cada dia, dos primeiros dias até depois da última taxa, é o saldo que o motor dá para o dia: aporte do dono,
    // a 100% e a 105%, e um aporte que começa depois.
    [Theory]
    [InlineData(609_731, 2026, 7, 21, 1_000_000)]
    [InlineData(479_731, 2026, 7, 21, 1_050_000)]
    [InlineData(1_047_408, 2026, 7, 22, 1_000_000)]
    [InlineData(900_000, 2026, 8, 10, 1_100_000)]
    [InlineData(100, 2026, 10, 15, 1_000_000)]
    public void Each_point_is_exactly_the_balance_the_engine_gives_for_that_day(long cents, int year, int month, int day, long percent)
    {
        var deposit = new DateOnly(year, month, day);
        var dates = EveryDay(new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 25));

        var series = Series(cents, deposit, new IndexPercent(percent), dates);

        series.Count.ShouldBe(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            if (dates[i] < deposit)
                series[i].ShouldBeNull($"antes do aporte, em {dates[i]}");
            else
                series[i].ShouldBe(
                    FixedIncomeEngine.Balance(new Money(cents), deposit, new IndexPercent(percent), Cdi2026.History, dates[i]).Value.GrossBalance,
                    $"em {dates[i]}");
        }
    }

    [Fact]
    public void Before_the_deposit_there_is_no_balance_and_on_the_deposit_day_the_balance_is_the_principal()
    {
        var series = Series(609_731, Start, Cdi100, [Start.AddDays(-5), Start.AddDays(-1), Start, Start.AddDays(1)]);

        series[0].ShouldBeNull();
        series[1].ShouldBeNull();
        series[2].ShouldBe(new Money(609_731));
        series[3].ShouldBe(new Money(610_051)); // um dia de taxa (0,052531 %): 609.731 × 1,00052531 = 610.051,3
    }

    // Os vetores independentes de FixedIncomeEngineTests, agora pela série.
    [Fact]
    public void The_series_reproduces_the_independent_calculation_of_the_owner_simulation()
    {
        var series = Series(
            609_731, Start, Cdi100,
            [new(2026, 7, 31), new(2026, 8, 31), new(2026, 9, 30), new(2026, 10, 16)]);

        series.ShouldBe([new Money(612_298), new Money(618_997), new Money(625_698), new Money(629_202)]);
    }

    [Fact]
    public void An_empty_list_of_dates_gives_an_empty_series() => Series(609_731, Start, Cdi100, []).ShouldBeEmpty();

    [Fact]
    public void The_dates_must_be_in_strictly_ascending_order()
    {
        Should.Throw<ArgumentException>(() => Series(609_731, Start, Cdi100, [Start.AddDays(2), Start.AddDays(1)]));
        Should.Throw<ArgumentException>(() => Series(609_731, Start, Cdi100, [Start.AddDays(1), Start.AddDays(1)]));
    }

    [Fact]
    public void A_series_refuses_what_the_balance_refuses()
    {
        FixedIncomeEngine.Series(new Money(0), Start, Cdi100, Cdi2026.History, [Start]).IsSuccess.ShouldBeFalse();
        FixedIncomeEngine.Series(new Money(1_000), Start, new IndexPercent(0), Cdi2026.History, [Start]).IsSuccess.ShouldBeFalse();
        var early = FixedIncomeEngine.Series(new Money(1_000), Start.AddDays(-1), Cdi100, Cdi2026.History, [Start]);
        early.Error!.Code.ShouldBe("RatesUnavailable");
        FixedIncomeEngine.Series(new Money(1_000), Start, Cdi100, RateHistory.Create([]).Value, [Start]).Error!.Code
            .ShouldBe("RatesUnavailable");
    }

    // O fator fora do limite é recusado como no saldo.
    [Fact]
    public void A_runaway_factor_is_refused_like_in_the_balance()
    {
        var rates = RateHistory.Create(Enumerable.Range(0, 400).Select(i => new DailyRate(Start.AddDays(i), 999_999))).Value;

        var series = FixedIncomeEngine.Series(new Money(1_000), Start, new IndexPercent(IndexPercent.Max), rates, [Start.AddDays(399)]);

        series.IsSuccess.ShouldBeFalse();
    }

    // ---- Propriedades ----

    private static readonly Gen<List<long>> Rates = Gen.Long[1, 150_000].List[1, 300];

    private static RateHistory HistoryOf(List<long> rates) =>
        RateHistory.Create(rates.Select((r, i) => new DailyRate(new DateOnly(2026, 1, 2).AddDays(i), r))).Value;

    [Fact]
    public void For_any_input_each_point_equals_the_engine_balance_and_the_series_never_decreases() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[1, 80], Gen.Int[100, 5_000_000], Gen.Long[1, 100_000_000_00])
            .Sample((rates, depositSeed, count, percent, cents) =>
            {
                var history = HistoryOf(rates);
                var start = new DateOnly(2026, 1, 2);
                var deposit = start.AddDays(depositSeed % rates.Count);
                // Datas crescentes a partir de uma data antes do aporte, com saltos diferentes.
                var dates = Enumerable.Range(0, count).Select(i => start.AddDays((depositSeed % 5) + i * ((depositSeed % 7) + 1))).ToList();

                var series = FixedIncomeEngine.Series(new Money(cents), deposit, new IndexPercent(percent), history, dates).Value;

                Money? previous = null;
                for (var i = 0; i < dates.Count; i++)
                {
                    if (dates[i] < deposit)
                    {
                        if (series[i] is not null)
                            return false;
                        continue;
                    }

                    var expected = FixedIncomeEngine.Balance(new Money(cents), deposit, new IndexPercent(percent), history, dates[i]).Value.GrossBalance;
                    if (series[i] != expected || (previous is { } p && series[i]!.Value.Cents < p.Cents))
                        return false;
                    previous = series[i];
                }

                return true;
            });
}
