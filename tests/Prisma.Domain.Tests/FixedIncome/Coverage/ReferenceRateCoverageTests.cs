using CsCheck;
using Prisma.Domain.Calendar;
using Prisma.Domain.FixedIncome.Coverage;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome.Coverage;

// Lacuna e defasagem do CDI (docs/renda-fixa.md, 3.3). O calendário bancário só serve para dizer quais dias a série deveria
// ter; o cálculo do rendimento nunca o usa e só olha as datas gravadas. Conferido com dados reais: de 02/01/2023 a 15/10/2026
// o Banco Central publicou 951 datas, e nenhum dia útil do calendário ficou sem taxa.
public sealed class ReferenceRateCoverageTests
{
    private static IEnumerable<DateOnly> RealDates => Cdi2026.Rates.Select(r => r.Date);

    // ---- Lacuna ----

    [Fact]
    public void The_real_series_has_no_gap()
    {
        var coverage = ReferenceRateCoverage.Analyze(RealDates, new DateOnly(2026, 10, 16));

        coverage.First.ShouldBe(new DateOnly(2026, 7, 21));
        coverage.Last.ShouldBe(new DateOnly(2026, 10, 15));
        coverage.MissingBusinessDays.ShouldBeEmpty();
        coverage.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void A_business_day_without_a_rate_is_a_gap()
    {
        var dates = RealDates.Where(d => d != new DateOnly(2026, 9, 2) && d != new DateOnly(2026, 9, 3));

        var coverage = ReferenceRateCoverage.Analyze(dates, new DateOnly(2026, 10, 16));

        coverage.MissingBusinessDays.ShouldBe([new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 3)]);
        coverage.IsComplete.ShouldBeFalse();
    }

    // Fim de semana e feriado sem taxa não são lacuna: 10 e 11/10 (fim de semana) e 12/10 (feriado) não estão na série real.
    [Fact]
    public void Weekends_and_holidays_are_not_gaps()
    {
        var dates = new[] { new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 13) };

        ReferenceRateCoverage.Analyze(dates, new DateOnly(2026, 10, 14)).MissingBusinessDays.ShouldBeEmpty();
    }

    // Carnaval 2026 (16 e 17/02), Sexta-feira Santa (03/04) e Corpus Christi (04/06) não têm taxa e não são lacuna.
    [Fact]
    public void Carnival_and_the_movable_holidays_are_not_gaps()
    {
        var carnival = new[] { new DateOnly(2026, 2, 13), new DateOnly(2026, 2, 18) };
        var easter = new[] { new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 6) };
        var corpusChristi = new[] { new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 5) };

        ReferenceRateCoverage.Analyze(carnival, new DateOnly(2026, 2, 19)).MissingBusinessDays.ShouldBeEmpty();
        ReferenceRateCoverage.Analyze(easter, new DateOnly(2026, 4, 7)).MissingBusinessDays.ShouldBeEmpty();
        ReferenceRateCoverage.Analyze(corpusChristi, new DateOnly(2026, 6, 8)).MissingBusinessDays.ShouldBeEmpty();
    }

    // O dia de hoje e o futuro não são lacuna: só entre a primeira e a última data gravadas.
    [Fact]
    public void Only_the_stretch_between_the_first_and_the_last_stored_date_can_have_gaps()
    {
        var coverage = ReferenceRateCoverage.Analyze([new DateOnly(2026, 10, 13)], new DateOnly(2026, 10, 20));

        coverage.MissingBusinessDays.ShouldBeEmpty();
    }

    // ---- Defasagem ----

    // O CDI de um dia sai à noite. De manhã, o que deveria estar gravado é o do último dia útil ANTES de hoje.
    [Theory]
    [InlineData(2026, 10, 16, 2026, 10, 15)] // sexta: quinta
    [InlineData(2026, 10, 19, 2026, 10, 16)] // segunda: sexta
    [InlineData(2026, 10, 17, 2026, 10, 16)] // sábado: sexta
    [InlineData(2026, 10, 18, 2026, 10, 16)] // domingo: sexta
    [InlineData(2026, 10, 13, 2026, 10, 9)] // terça depois do feriado de 12/10 (segunda): sexta 09/10
    [InlineData(2026, 11, 3, 2026, 10, 30)] // terça depois de Finados (segunda 02/11): sexta
    [InlineData(2026, 2, 18, 2026, 2, 13)] // quarta de Cinzas depois do Carnaval: sexta
    [InlineData(2026, 1, 2, 2025, 12, 31)] // 31/12 é dia útil
    public void The_expected_last_date_is_the_previous_business_day(
        int year, int month, int day, int expectedYear, int expectedMonth, int expectedDay) =>
        ReferenceRateCoverage.ExpectedLastDate(new DateOnly(year, month, day))
            .ShouldBe(new DateOnly(expectedYear, expectedMonth, expectedDay));

    // O último dia em que se pode resgatar um aporte do CDI: aquele cujo dia útil anterior já tem taxa gravada.
    [Theory]
    [InlineData(2026, 10, 15, 2026, 10, 19, 2026, 10, 16)] // quinta com taxa, segunda hoje: sexta (a de sexta ainda não saiu)
    [InlineData(2026, 10, 16, 2026, 10, 19, 2026, 10, 19)] // sexta com taxa, segunda hoje: hoje mesmo
    [InlineData(2026, 10, 16, 2026, 10, 17, 2026, 10, 17)] // sábado: o dia útil anterior é a sexta, que tem taxa
    [InlineData(2026, 10, 14, 2026, 10, 19, 2026, 10, 15)] // taxas atrasadas dois dias: só até quinta
    [InlineData(2026, 10, 15, 2026, 10, 15, 2026, 10, 15)] // nunca passa de hoje
    public void The_latest_redemption_date_is_the_first_day_whose_previous_business_day_has_a_rate_and_never_after_today(
        int ly, int lm, int ld, int ty, int tm, int td, int ey, int em, int ed) =>
        ReferenceRateCoverage.LatestRedemptionDate(new DateOnly(ly, lm, ld), new DateOnly(ty, tm, td))
            .ShouldBe(new DateOnly(ey, em, ed));

    // A data devolvida sempre passa na regra do resgate: o dia útil anterior a ela tem taxa, e a data seguinte (se couber) não tem.
    [Fact]
    public void The_latest_redemption_date_is_always_redeemable_and_the_next_one_is_not()
    {
        Gen.Select(AnyDay, Gen.Int[0, 10]).Sample((last, behind) =>
        {
            var lastRate = last;
            while (!BankCalendar.IsBusinessDay(lastRate))
                lastRate = lastRate.AddDays(-1);
            var today = lastRate.AddDays(1 + behind);

            var latest = ReferenceRateCoverage.LatestRedemptionDate(lastRate, today);

            var redeemable = ReferenceRateCoverage.ExpectedLastDate(latest) <= lastRate && latest <= today;
            var nextFails = latest >= today || ReferenceRateCoverage.ExpectedLastDate(latest.AddDays(1)) > lastRate;
            return redeemable && nextFails;
        });
    }

    [Fact]
    public void It_is_current_when_the_last_stored_date_is_the_expected_one()
    {
        var coverage = ReferenceRateCoverage.Analyze(RealDates, new DateOnly(2026, 10, 16));

        coverage.ExpectedLast.ShouldBe(new DateOnly(2026, 10, 15));
        coverage.IsStale.ShouldBeFalse();
    }

    // O que valia em 17/10/2026 (sábado): a taxa de sexta ainda não tinha saído.
    [Fact]
    public void It_is_stale_when_the_last_stored_date_is_older_than_expected()
    {
        var coverage = ReferenceRateCoverage.Analyze(RealDates, new DateOnly(2026, 10, 17));

        coverage.ExpectedLast.ShouldBe(new DateOnly(2026, 10, 16));
        coverage.IsStale.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_stored_is_stale_and_has_no_range()
    {
        var coverage = ReferenceRateCoverage.Analyze([], new DateOnly(2026, 10, 16));

        coverage.IsStale.ShouldBeTrue();
        (coverage.First, coverage.Last).ShouldBe((null, null));
        coverage.MissingBusinessDays.ShouldBeEmpty();
    }

    // A ordem e a repetição das datas de entrada não importam.
    [Fact]
    public void Input_order_and_repeats_do_not_matter()
    {
        var shuffled = RealDates.Reverse().Concat(RealDates.Take(5));

        var coverage = ReferenceRateCoverage.Analyze(shuffled, new DateOnly(2026, 10, 16));

        coverage.First.ShouldBe(new DateOnly(2026, 7, 21));
        coverage.Last.ShouldBe(new DateOnly(2026, 10, 15));
        coverage.MissingBusinessDays.ShouldBeEmpty();
    }

    // ---- Propriedades ----

    private static readonly Gen<DateOnly> AnyDay = Gen.Int[0, 365 * 20].Select(n => new DateOnly(2015, 1, 1).AddDays(n));

    [Fact]
    public void The_expected_last_date_is_always_a_business_day_strictly_before_today_with_none_in_between() =>
        AnyDay.Sample(today =>
        {
            var expected = ReferenceRateCoverage.ExpectedLastDate(today);
            var between = Enumerable.Range(expected.DayNumber + 1, today.DayNumber - expected.DayNumber - 1)
                .Select(DateOnly.FromDayNumber);

            return BankCalendar.IsBusinessDay(expected) && expected < today && !between.Any(BankCalendar.IsBusinessDay);
        });

    // Gravar todos os dias úteis de um período nunca deixa lacuna, em qualquer período.
    [Fact]
    public void Every_business_day_stored_means_no_gap() =>
        Gen.Select(AnyDay, Gen.Int[1, 800]).Sample((start, length) =>
        {
            var dates = Enumerable.Range(0, length).Select(start.AddDays).Where(BankCalendar.IsBusinessDay).ToList();

            return ReferenceRateCoverage.Analyze(dates, start.AddDays(length + 1)).IsComplete;
        });

    // Tirar um dia útil do meio sempre aparece como lacuna, e só ele.
    [Fact]
    public void A_removed_business_day_in_the_middle_is_reported_alone() =>
        Gen.Select(AnyDay, Gen.Int[10, 400], Gen.Int[1, 8]).Sample((start, length, offset) =>
        {
            var dates = Enumerable.Range(0, length).Select(start.AddDays).Where(BankCalendar.IsBusinessDay).ToList();
            if (dates.Count < 4)
                return true;

            var removed = dates[Math.Clamp(offset, 1, dates.Count - 2)];
            var coverage = ReferenceRateCoverage.Analyze(dates.Where(d => d != removed), start.AddDays(length + 1));

            return coverage.MissingBusinessDays.SequenceEqual([removed]);
        });
}
