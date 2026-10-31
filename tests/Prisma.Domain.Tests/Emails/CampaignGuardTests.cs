using Prisma.Domain.Emails;
using Shouldly;

namespace Prisma.Domain.Tests.Emails;

// docs/admin-email.md, 4 (AE.4): as travas de um disparo, na ordem em que o dono as encontra.
public sealed class CampaignGuardTests
{
    private const int Cap = 50;

    private static Result<int> Check(
        CampaignCategory category = CampaignCategory.Account, bool unsubscribe = false, int eligible = 12, int expected = 12) =>
        CampaignGuard.Check(category, unsubscribe, eligible, expected, Cap);

    [Fact]
    public void An_account_email_goes_to_exactly_the_people_the_owner_saw() =>
        Check().Value.ShouldBe(12);

    [Fact]
    public void A_relationship_email_is_blocked_while_there_is_no_unsubscribe()
    {
        var result = Check(CampaignCategory.Relationship);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("UnsubscribeRequired");
    }

    [Fact]
    public void A_relationship_email_goes_once_the_unsubscribe_exists() =>
        Check(CampaignCategory.Relationship, unsubscribe: true).Value.ShouldBe(12);

    [Fact]
    public void An_account_email_does_not_need_the_unsubscribe() =>
        Check(CampaignCategory.Account, unsubscribe: false).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Nobody_in_the_audience_is_refused()
    {
        var result = Check(eligible: 0, expected: 0);

        result.Error!.Code.ShouldBe("NoRecipients");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void An_audience_over_the_cap_is_refused()
    {
        var result = Check(eligible: Cap + 1, expected: Cap + 1);

        result.Error!.Code.ShouldBe("TooManyRecipients");
        result.Error.Message.ShouldContain($"{Cap}");
    }

    [Fact]
    public void An_audience_exactly_at_the_cap_goes() =>
        Check(eligible: Cap, expected: Cap).Value.ShouldBe(Cap);

    [Theory]
    [InlineData(12, 11)]
    [InlineData(12, 13)]
    [InlineData(12, 0)]
    [InlineData(12, -1)]
    public void A_count_the_owner_did_not_see_is_a_conflict(int eligible, int expected)
    {
        var result = Check(eligible: eligible, expected: expected);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("AudienceChanged");
        result.Error.Message.ShouldContain($"{eligible}");
    }

    // A ordem importa para a mensagem: o bloqueio do modelo vem antes de olhar o público.
    [Fact]
    public void The_block_of_the_template_wins_over_an_empty_audience() =>
        Check(CampaignCategory.Relationship, eligible: 0, expected: 0).Error!.Code.ShouldBe("UnsubscribeRequired");

    [Fact]
    public void An_empty_audience_wins_over_a_stale_count() =>
        Check(eligible: 0, expected: 5).Error!.Code.ShouldBe("NoRecipients");

    [Fact]
    public void Too_many_wins_over_a_stale_count() =>
        Check(eligible: Cap + 3, expected: 5).Error!.Code.ShouldBe("TooManyRecipients");
}
