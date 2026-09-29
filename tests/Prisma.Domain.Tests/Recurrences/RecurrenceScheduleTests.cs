using Prisma.Domain.Recurrences;
using Shouldly;

namespace Prisma.Domain.Tests.Recurrences;

// docs/fase-2.md, 2.14, regra 3 e a tabela "Agenda". A partida é a ocorrência 0 (o lançamento que criou
// a série); a agenda é sempre calculada a partir dela. Fim de semana e feriado não mudam a data.
public sealed class RecurrenceScheduleTests
{
    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    [Fact]
    public void Monthly_pet_on_the_card_keeps_day_25_even_on_a_sunday()
    {
        var start = D(25, 9);

        RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Monthly, 0).ShouldBe(start);
        RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Monthly, 1).ShouldBe(D(25, 10)); // domingo
        RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Monthly, 2).ShouldBe(D(25, 11));
        RecurrenceSchedule.Occurrence(start, RecurrenceFrequency.Monthly, 3).ShouldBe(D(25, 12));
    }

    [Fact]
    public void Monthly_rent_keeps_day_10_even_on_a_saturday() =>
        RecurrenceSchedule.Occurrence(D(10, 9), RecurrenceFrequency.Monthly, 1).ShouldBe(D(10, 10));

    [Fact]
    public void Weekly_cleaner_walks_seven_days_from_the_start() =>
        Enumerable.Range(1, 5).Select(k => RecurrenceSchedule.Occurrence(D(2, 10), RecurrenceFrequency.Weekly, k))
            .ShouldBe([D(9, 10), D(16, 10), D(23, 10), D(30, 10), D(6, 11)]);

    // Sempre a partir da partida: depois de 30/11, volta a 31 (e não fica em 30 para sempre).
    [Fact]
    public void Day_31_becomes_the_last_day_of_shorter_months_and_comes_back() =>
        Enumerable.Range(1, 5).Select(k => RecurrenceSchedule.Occurrence(D(31, 10), RecurrenceFrequency.Monthly, k))
            .ShouldBe([D(30, 11), D(31, 12), D(31, 1, 2027), D(28, 2, 2027), D(31, 3, 2027)]);

    [Fact]
    public void Day_31_in_a_leap_year_february_is_the_29th() =>
        RecurrenceSchedule.Occurrence(D(31, 1, 2028), RecurrenceFrequency.Monthly, 1).ShouldBe(D(29, 2, 2028));

    // O intervalo exclui a data de partida da geração (generated_through) e inclui a de chegada (hoje).
    [Fact]
    public void Between_excludes_after_and_includes_until() =>
        RecurrenceSchedule.Between(D(25, 9), RecurrenceFrequency.Monthly, end: null, after: D(25, 9), until: D(25, 11))
            .ShouldBe([D(25, 10), D(25, 11)]);

    [Fact]
    public void Between_is_empty_before_the_next_occurrence() =>
        RecurrenceSchedule.Between(D(25, 9), RecurrenceFrequency.Monthly, end: null, after: D(25, 9), until: D(24, 10))
            .ShouldBeEmpty();

    // Pet com término em 30/11: 25/10 e 25/11; 25/12 não.
    [Fact]
    public void Between_stops_at_the_end_date() =>
        RecurrenceSchedule.Between(D(25, 9), RecurrenceFrequency.Monthly, end: D(30, 11), after: D(25, 9), until: D(31, 12))
            .ShouldBe([D(25, 10), D(25, 11)]);

    [Fact]
    public void Between_includes_an_occurrence_on_the_end_date() =>
        RecurrenceSchedule.Between(D(25, 9), RecurrenceFrequency.Monthly, end: D(25, 11), after: D(25, 9), until: D(31, 12))
            .ShouldBe([D(25, 10), D(25, 11)]);

    // A geração atrasada (API fora do ar) alcança o que faltou de uma vez.
    [Fact]
    public void Between_catches_up_every_missed_week() =>
        RecurrenceSchedule.Between(D(2, 10), RecurrenceFrequency.Weekly, end: null, after: D(9, 10), until: D(28, 10))
            .ShouldBe([D(16, 10), D(23, 10)]);

    [Fact]
    public void Next_is_the_first_occurrence_after_a_date()
    {
        RecurrenceSchedule.Next(D(25, 9), RecurrenceFrequency.Monthly, end: null, after: D(25, 10)).ShouldBe(D(25, 11));
        RecurrenceSchedule.Next(D(25, 9), RecurrenceFrequency.Monthly, end: null, after: D(1, 10)).ShouldBe(D(25, 10));
        RecurrenceSchedule.Next(D(31, 10), RecurrenceFrequency.Monthly, end: null, after: D(30, 11)).ShouldBe(D(31, 12));
    }

    [Fact]
    public void Next_is_null_after_the_end_date() =>
        RecurrenceSchedule.Next(D(25, 9), RecurrenceFrequency.Monthly, end: D(30, 11), after: D(25, 11)).ShouldBeNull();
}
