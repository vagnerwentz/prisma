using CsCheck;
using Prisma.Domain.Accounts;
using Shouldly;

namespace Prisma.Domain.Tests.Accounts;

// docs/admin-email.md, AE.2: "ativa" é quem foi vista há menos de N dias, "inativa" há N dias ou mais, contando dias civis
// de São Paulo (hoje − dia do último acesso). Hoje, vista ontem, vista há N−1 dias é ativa; há N dias, inativa.
public sealed class ActivityWindowTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static bool IsActive(DateTime lastSeenUtc, int days) =>
        ActivityWindow.Of(days).Value.IsActive(lastSeenUtc, Today);

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void Days_from_1_to_365_are_valid(int days) =>
        ActivityWindow.Of(days).Value.Days.ShouldBe(days);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    [InlineData(int.MaxValue)]
    public void Days_outside_1_to_365_are_refused(int days)
    {
        var result = ActivityWindow.Of(days);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("InvalidDays");
        result.Error.Message.ShouldContain("365");
    }

    [Fact]
    public void Default_is_30_days() => ActivityWindow.Default.Days.ShouldBe(30);

    [Fact]
    public void Seen_today_is_active_even_for_a_window_of_one_day() =>
        IsActive(Utc(2026, 10, 19, 15, 0), 1).ShouldBeTrue();

    [Fact]
    public void Seen_N_minus_1_days_ago_is_active_and_N_days_ago_is_inactive()
    {
        // Hoje é 19/10; 29 dias antes é 20/09, e 30 dias antes é 19/09 (meio-dia em São Paulo).
        IsActive(Utc(2026, 9, 20, 15, 0), 30).ShouldBeTrue();
        IsActive(Utc(2026, 9, 19, 15, 0), 30).ShouldBeFalse();
    }

    [Fact]
    public void The_day_changes_at_midnight_in_Sao_Paulo_not_in_UTC()
    {
        // 23:30 de 18/10 em São Paulo é 02:30 UTC de 19/10: para a pessoa, ainda é ontem (inativa numa janela de 1 dia).
        IsActive(Utc(2026, 10, 19, 2, 30), 1).ShouldBeFalse();
        // 00:10 de 19/10 em São Paulo é 03:10 UTC: já é hoje.
        IsActive(Utc(2026, 10, 19, 3, 10), 1).ShouldBeTrue();
    }

    [Fact]
    public void Active_since_is_the_start_of_the_oldest_day_that_still_counts() =>
        ActivityWindow.Of(30).Value.ActiveSince(Today).ShouldBe(SaoPauloTime.StartOf(new DateOnly(2026, 9, 20)));

    // A propriedade que o SQL do admin precisa: comparar o instante com ActiveSince dá a mesma resposta que contar os dias
    // civis entre o último acesso e hoje. Se divergissem, a contagem da tela e a regra descrita aqui seriam coisas diferentes.
    [Fact]
    public void Comparing_with_ActiveSince_agrees_with_counting_civil_days()
    {
        Gen.Select(Gen.Int[1, 365], Gen.Int[0, 800 * 24 * 60])
            .Sample((days, minutesAgo) =>
            {
                var now = Utc(2026, 10, 19, 15, 0);
                var lastSeen = now.AddMinutes(-minutesAgo);
                var window = ActivityWindow.Of(days).Value;

                var byInstant = lastSeen >= window.ActiveSince(Today);
                var byCivilDays = Today.DayNumber - SaoPauloTime.DateOf(lastSeen).DayNumber < days;

                byInstant.ShouldBe(byCivilDays);
                window.IsActive(lastSeen, Today).ShouldBe(byCivilDays);
            });
    }
}
