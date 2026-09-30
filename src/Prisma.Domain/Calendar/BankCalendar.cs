namespace Prisma.Domain.Calendar;

// Calendário bancário nacional (docs/fase-2.md, 2.15, regras do calendário). Dia útil é o que não é
// sábado, domingo nem feriado nacional: os fixos e os móveis calculados a partir da Páscoa. Não há tabela
// de feriados para manter; feriado estadual e municipal ficam de fora. Só o débito automático e o
// vencimento da fatura (2.22) usam este calendário (teste de arquitetura): o resto fica na data em que
// aconteceu.
public static class BankCalendar
{
    private static readonly (int Month, int Day)[] FixedHolidays =
    [
        (1, 1),   // Confraternização Universal
        (4, 21),  // Tiradentes
        (5, 1),   // Dia do Trabalho
        (9, 7),   // Independência
        (10, 12), // Nossa Senhora Aparecida
        (11, 2),  // Finados
        (11, 15), // Proclamação da República
        (11, 20), // Consciência Negra, nacional desde 2024
        (12, 25), // Natal
    ];

    // Em dias a partir da Páscoa. Corpus Christi não é feriado em lei, mas os bancos não abrem (o Itaú
    // o tratou como não útil em 04/06/2026). A Quarta-feira de Cinzas é útil.
    private static readonly int[] EasterOffsets =
    [
        -48, // segunda de Carnaval
        -47, // terça de Carnaval
        -2,  // Sexta-feira Santa
        60,  // Corpus Christi
    ];

    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    // O próprio dia, se útil; senão o primeiro dia útil depois dele.
    public static DateOnly BusinessDayOnOrAfter(DateOnly date)
    {
        while (!IsBusinessDay(date))
            date = date.AddDays(1);
        return date;
    }

    private static bool IsHoliday(DateOnly date)
    {
        if (FixedHolidays.Contains((date.Month, date.Day)))
            return true;

        var fromEaster = date.DayNumber - Easter(date.Year).DayNumber;
        return EasterOffsets.Contains(fromEaster);
    }

    // Domingo de Páscoa no calendário gregoriano (algoritmo anônimo, de Meeus/Jones/Butcher).
    private static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
