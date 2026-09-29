using CsCheck;
using Prisma.Domain.Recurrences;

namespace Prisma.Domain.Tests.Recurrences;

// docs/fase-2.md, 2.14, regra 3, para quaisquer partidas e índices: a série mensal nunca escorrega de
// dia, nunca repete nem pula mês; a semanal anda sempre 7 dias; o intervalo é crescente e sem repetição.
public sealed class RecurrenceSchedulePropertyTests
{
    private static readonly Gen<DateOnly> Start = Gen.Int[0, 365 * 40].Select(offset => new DateOnly(2000, 1, 1).AddDays(offset));

    private static int MonthIndex(DateOnly d) => d.Year * 12 + d.Month - 1;

    [Fact]
    public void Monthly_occurrence_k_is_k_months_later_on_the_start_day_or_the_last_day() =>
        Gen.Select(Start, Gen.Int[0, 120]).Sample((start, k) =>
        {
            var occurrence = RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Monthly, k);
            var daysInMonth = DateTime.DaysInMonth(occurrence.Year, occurrence.Month);
            return MonthIndex(occurrence) == MonthIndex(start) + k
                && occurrence.Day == Math.Min(start.Day, daysInMonth);
        });

    [Fact]
    public void Weekly_occurrences_are_seven_days_apart() =>
        Gen.Select(Start, Gen.Int[0, 520]).Sample((start, k) =>
            RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Weekly, k + 1).DayNumber
            - RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Weekly, k).DayNumber == 7);

    [Fact]
    public void Between_is_increasing_inside_the_window_and_matches_the_occurrences() =>
        Gen.Select(Start, Gen.Int[0, 800], Gen.Int[0, 400], Gen.Bool).Sample((start, skip, span, monthly) =>
        {
            var frequency = monthly ? RecurrenceFrequency.Monthly : RecurrenceFrequency.Weekly;
            var after = start.AddDays(skip);
            var until = after.AddDays(span);
            var dates = RecurrenceSchedule.Between(start, frequency, end: null, after, until).ToList();

            var expected = Enumerable.Range(0, 2000)
                .Select(k => RecurrenceSchedule.Occurrence(start, frequency, k))
                .Where(d => d > after && d <= until);
            return dates.SequenceEqual(expected);
        });
}
