using Shouldly;

namespace Prisma.Domain.Tests;

public sealed class SaoPauloTimeTests
{
    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Purchase_at_23h30_in_Sao_Paulo_stays_on_the_same_day()
    {
        // 15/03/2026 23:30 em São Paulo (UTC-3) é 16/03/2026 02:30 em UTC.
        IClock clock = new FakeClock(Utc(2026, 3, 16, 2, 30));

        clock.Today.ShouldBe(new DateOnly(2026, 3, 15));
    }

    [Theory]
    [InlineData(2, 59, 15)] // 23:59 do dia 15 em São Paulo
    [InlineData(3, 0, 16)]  // 00:00 do dia 16 em São Paulo
    public void Day_changes_at_midnight_in_Sao_Paulo_not_in_UTC(int utcHour, int utcMinute, int expectedDay) =>
        SaoPauloTime.DateOf(Utc(2026, 3, 16, utcHour, utcMinute))
            .ShouldBe(new DateOnly(2026, 3, expectedDay));

    // Até 2019 o Brasil tinha horário de verão (UTC-2). Usar o fuso da base IANA, e não um
    // deslocamento fixo de -3, mantém correta a data de dados antigos importados.
    [Fact]
    public void Uses_historical_daylight_saving_rules()
    {
        // 01/12/2018 23:30 em São Paulo, no horário de verão, é 02/12/2018 01:30 em UTC.
        SaoPauloTime.DateOf(Utc(2018, 12, 2, 1, 30)).ShouldBe(new DateOnly(2018, 12, 1));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Rejects_instants_that_are_not_UTC(DateTimeKind kind) =>
        Should.Throw<ArgumentException>(() =>
            SaoPauloTime.DateOf(new DateTime(2026, 3, 16, 2, 30, 0, kind)));

    [Fact]
    public void Clock_is_replaceable_and_Today_follows_it()
    {
        var fake = new FakeClock(Utc(2026, 3, 16, 2, 30));
        IClock clock = fake;
        clock.Today.ShouldBe(new DateOnly(2026, 3, 15));

        fake.UtcNow = Utc(2026, 3, 16, 3, 0);

        clock.Today.ShouldBe(new DateOnly(2026, 3, 16));
    }
}
