using CsCheck;
using Prisma.Domain.Calendar;

namespace Prisma.Domain.Tests.Calendar;

// docs/fase-2.md, 2.15, regra 5 do calendário, para quaisquer datas de 2000 a 2099: o próximo dia útil é
// útil, nunca vem antes da data e não pula nenhum dia útil; a função nunca volta no tempo (a regra 3 do
// débito automático depende disso para manter a ordem das ocorrências).
public sealed class BankCalendarPropertyTests
{
    private static readonly Gen<DateOnly> Date =
        Gen.Int[0, 365 * 100].Select(offset => new DateOnly(2000, 1, 1).AddDays(offset));

    [Fact]
    public void Result_is_a_business_day_not_before_the_date() =>
        Date.Sample(date =>
        {
            var result = BankCalendar.BusinessDayOnOrAfter(date);
            return result >= date && BankCalendar.IsBusinessDay(result);
        });

    [Fact]
    public void No_business_day_is_skipped() =>
        Date.Sample(date =>
        {
            var result = BankCalendar.BusinessDayOnOrAfter(date);
            for (var day = date; day < result; day = day.AddDays(1))
            {
                if (BankCalendar.IsBusinessDay(day))
                    return false;
            }

            return true;
        });

    [Fact]
    public void Never_goes_back_in_time() =>
        Gen.Select(Date, Gen.Int[0, 60]).Sample((date, gap) =>
            BankCalendar.BusinessDayOnOrAfter(date) <= BankCalendar.BusinessDayOnOrAfter(date.AddDays(gap)));

    // A folga de 7 dias do vencimento da primeira ocorrência (2.15, regra 4 do débito) cobre o maior
    // adiamento real: 4 dias, do sábado antes do Carnaval à Quarta-feira de Cinzas.
    [Fact]
    public void Never_postpones_more_than_four_days() =>
        Date.Sample(date => BankCalendar.BusinessDayOnOrAfter(date).DayNumber - date.DayNumber <= 4);
}
