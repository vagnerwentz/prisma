using Prisma.Domain.Calendar;
using Shouldly;

namespace Prisma.Domain.Tests.Calendar;

// docs/fase-2.md, 2.15, regras do calendário e a tabela "Calendário". Os valores esperados saem da
// especificação (datas conferidas no calendário de 2025 a 2027), não do código.
public sealed class BankCalendarTests
{
    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    [Theory]
    [InlineData(1, 1)]   // Confraternização Universal
    [InlineData(21, 4)]  // Tiradentes
    [InlineData(1, 5)]   // Dia do Trabalho
    [InlineData(7, 9)]   // Independência
    [InlineData(12, 10)] // Nossa Senhora Aparecida
    [InlineData(2, 11)]  // Finados
    [InlineData(15, 11)] // Proclamação da República
    [InlineData(20, 11)] // Consciência Negra, nacional desde 2024
    [InlineData(25, 12)] // Natal
    public void Fixed_national_holidays_are_not_business_days(int day, int month)
    {
        foreach (var year in new[] { 2025, 2026, 2027 })
            BankCalendar.IsBusinessDay(D(day, month, year)).ShouldBeFalse($"{day:00}/{month:00}/{year}");
    }

    // Carnaval (segunda e terça), Sexta-feira Santa e Corpus Christi, a partir da Páscoa:
    // 20/04/2025, 05/04/2026 e 28/03/2027.
    [Theory]
    [InlineData(3, 3, 2025)]
    [InlineData(4, 3, 2025)]
    [InlineData(18, 4, 2025)]
    [InlineData(19, 6, 2025)]
    [InlineData(16, 2, 2026)]
    [InlineData(17, 2, 2026)]
    [InlineData(3, 4, 2026)]
    [InlineData(4, 6, 2026)] // o Itaú tratou como não útil (docs/validacao-premissas.md, seção 11)
    [InlineData(8, 2, 2027)]
    [InlineData(9, 2, 2027)]
    [InlineData(26, 3, 2027)]
    [InlineData(27, 5, 2027)]
    public void Moving_holidays_are_not_business_days(int day, int month, int year) =>
        BankCalendar.IsBusinessDay(D(day, month, year)).ShouldBeFalse();

    [Theory]
    [InlineData(18, 2, 2026)] // Quarta-feira de Cinzas
    [InlineData(24, 12, 2026)] // véspera de Natal (regra 4)
    [InlineData(31, 12, 2026)] // último dia do ano (regra 4)
    [InlineData(5, 6, 2026)]  // sexta depois de Corpus Christi
    [InlineData(29, 9, 2026)] // terça comum
    public void Ordinary_weekdays_are_business_days(int day, int month, int year) =>
        BankCalendar.IsBusinessDay(D(day, month, year)).ShouldBeTrue();

    [Fact]
    public void Weekends_are_not_business_days()
    {
        BankCalendar.IsBusinessDay(D(3, 10)).ShouldBeFalse(); // sábado
        BankCalendar.IsBusinessDay(D(4, 10)).ShouldBeFalse(); // domingo
    }

    // A tabela "Calendário" da especificação.
    [Theory]
    [InlineData("2026-02-16", "2026-02-18")] // Carnaval: a quarta é útil
    [InlineData("2026-02-17", "2026-02-18")]
    [InlineData("2026-04-03", "2026-04-06")] // Sexta-feira Santa
    [InlineData("2026-06-04", "2026-06-05")] // Corpus Christi
    [InlineData("2026-10-31", "2026-11-03")] // sábado, domingo e Finados
    [InlineData("2026-11-20", "2026-11-23")] // Consciência Negra, numa sexta
    [InlineData("2026-12-25", "2026-12-28")] // Natal, numa sexta
    [InlineData("2026-12-31", "2026-12-31")] // útil
    [InlineData("2027-01-01", "2027-01-04")]
    [InlineData("2026-10-10", "2026-10-13")] // sábado, domingo e Nossa Senhora Aparecida
    [InlineData("2026-11-15", "2026-11-16")] // domingo e feriado ao mesmo tempo
    [InlineData("2026-02-14", "2026-02-18")] // sábado antes do Carnaval: o maior adiamento, 4 dias
    public void Business_day_on_or_after(string date, string expected) =>
        BankCalendar.BusinessDayOnOrAfter(DateOnly.Parse(date)).ShouldBe(DateOnly.Parse(expected));
}
