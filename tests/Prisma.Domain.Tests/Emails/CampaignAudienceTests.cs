using CsCheck;
using Prisma.Domain.Accounts;
using Prisma.Domain.Emails;
using Shouldly;

namespace Prisma.Domain.Tests.Emails;

// docs/admin-email.md, 4 (AE.4): quem entra em cada público de um disparo. A contagem é em dias civis de São Paulo, como a da tela
// de Pessoas (ActivityWindow): "há N dias ou mais" é hoje − o dia do fato >= N.
public sealed class CampaignAudienceTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static DateTime Utc(int year, int month, int day, int hour = 15, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ActivityWindow Days(int days) => ActivityWindow.Of(days).Value;

    // Hoje é 19/10; 3 dias antes é 16/10.
    [Fact]
    public void An_unconfirmed_signup_is_stuck_from_N_days_on_and_not_before()
    {
        CampaignAudience.IsStuckSignup(false, Utc(2026, 10, 16), Days(3), Today).ShouldBeTrue();  // 3 dias: já
        CampaignAudience.IsStuckSignup(false, Utc(2026, 10, 17), Days(3), Today).ShouldBeFalse(); // 2 dias: ainda não
        CampaignAudience.IsStuckSignup(false, Utc(2026, 9, 1), Days(3), Today).ShouldBeTrue();
    }

    [Fact]
    public void A_confirmed_account_is_never_a_stuck_signup() =>
        CampaignAudience.IsStuckSignup(true, Utc(2026, 1, 1), Days(3), Today).ShouldBeFalse();

    [Fact]
    public void A_signup_of_today_is_not_stuck_even_for_one_day() =>
        CampaignAudience.IsStuckSignup(false, Utc(2026, 10, 19, 12), Days(1), Today).ShouldBeFalse();

    [Fact]
    public void A_confirmed_account_is_inactive_from_N_days_without_access_and_not_before()
    {
        CampaignAudience.IsInactive(true, Utc(2026, 9, 19), Days(30), Today).ShouldBeTrue();  // 30 dias
        CampaignAudience.IsInactive(true, Utc(2026, 9, 20), Days(30), Today).ShouldBeFalse(); // 29 dias
    }

    [Fact]
    public void An_unconfirmed_account_is_never_inactive() =>
        CampaignAudience.IsInactive(false, Utc(2026, 1, 1), Days(30), Today).ShouldBeFalse();

    // Sem acesso gravado não é "sem acesso há N dias": é outra mensagem (nunca usou), e o carimbo da migration não prova nada.
    [Fact]
    public void An_account_with_no_recorded_access_is_not_inactive() =>
        CampaignAudience.IsInactive(true, null, Days(30), Today).ShouldBeFalse();

    [Fact]
    public void The_day_changes_at_midnight_in_Sao_Paulo_not_in_UTC()
    {
        // 23:30 de 18/10 em São Paulo é 02:30 UTC de 19/10: ainda é ontem, então já passou 1 dia.
        CampaignAudience.IsInactive(true, Utc(2026, 10, 19, 2, 30), Days(1), Today).ShouldBeTrue();
        CampaignAudience.IsInactive(true, Utc(2026, 10, 19, 3, 10), Days(1), Today).ShouldBeFalse();
    }

    // As duas metades do mesmo N não deixam buraco: quem não está ativo na janela está parado.
    [Fact]
    public void A_confirmed_account_with_access_is_either_active_or_inactive_never_both_never_neither()
    {
        Gen.Select(Gen.Int[1, 365], Gen.Int[0, 800 * 24 * 60])
            .Sample((days, minutesAgo) =>
            {
                var lastSeen = Utc(2026, 10, 19).AddMinutes(-minutesAgo);
                var window = Days(days);

                window.IsActive(lastSeen, Today).ShouldBe(!CampaignAudience.IsInactive(true, lastSeen, window, Today));
            });
    }
}

public sealed class CampaignCooldownTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Someone_never_sent_to_is_always_eligible()
    {
        CampaignCooldown.IsEligible(null, Now, null).ShouldBeTrue();
        CampaignCooldown.IsEligible(null, Now, 7).ShouldBeTrue();
    }

    [Fact]
    public void Once_per_account_never_sends_again() =>
        CampaignCooldown.IsEligible(Now.AddDays(-400), Now, null).ShouldBeFalse();

    [Fact]
    public void With_an_interval_it_sends_again_only_after_the_interval()
    {
        CampaignCooldown.IsEligible(Now.AddDays(-7), Now, 7).ShouldBeTrue();
        CampaignCooldown.IsEligible(Now.AddDays(-7).AddMinutes(1), Now, 7).ShouldBeFalse();
        CampaignCooldown.IsEligible(Now.AddHours(-1), Now, 7).ShouldBeFalse();
    }

    // O SQL do disparo compara com este instante em vez de chamar IsEligible linha a linha.
    [Fact]
    public void Comparing_with_the_cutoff_agrees_with_IsEligible()
    {
        Gen.Select(Gen.Int[1, 60], Gen.Int[0, 90 * 24 * 60])
            .Sample((minDays, minutesAgo) =>
            {
                var lastSent = Now.AddMinutes(-minutesAgo);
                var cutoff = CampaignCooldown.SentSince(Now, minDays)!.Value;

                (lastSent <= cutoff).ShouldBe(CampaignCooldown.IsEligible(lastSent, Now, minDays));
            });
    }

    [Fact]
    public void Once_per_account_has_no_cutoff() => CampaignCooldown.SentSince(Now, null).ShouldBeNull();
}
