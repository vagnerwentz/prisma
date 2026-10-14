using Prisma.Api.Infrastructure.Email;
using Shouldly;

namespace Prisma.Api.Tests.Email;

// docs/email.md, seção 3, regra 3: 30 s, 2 min, 10 min, 1 h, depois a cada 6 h, até 24 h desde o pedido.
public sealed class EmailRetryPolicyTests
{
    private static readonly DateTime Created = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 2 * 60)]
    [InlineData(3, 10 * 60)]
    [InlineData(4, 60 * 60)]
    [InlineData(5, 6 * 60 * 60)]
    [InlineData(6, 6 * 60 * 60)]
    public void Waits_longer_after_each_failure(int attempts, int seconds)
    {
        var now = Created.AddMinutes(5);

        EmailRetryPolicy.NextAttemptAt(Created, attempts, now).ShouldBe(now.AddSeconds(seconds));
    }

    [Fact]
    public void A_retry_exactly_at_24_hours_still_happens()
    {
        var now = Created.AddHours(18);

        EmailRetryPolicy.NextAttemptAt(Created, 6, now).ShouldBe(Created.AddHours(24));
    }

    [Fact]
    public void Gives_up_when_the_next_attempt_would_pass_24_hours()
    {
        var now = Created.AddHours(18).AddSeconds(1);

        EmailRetryPolicy.NextAttemptAt(Created, 6, now).ShouldBeNull();
    }

    [Fact]
    public void Gives_up_even_on_an_early_attempt_if_the_email_is_old()
    {
        // A API ficou um dia fora do ar: o e-mail da véspera não sai mais.
        EmailRetryPolicy.NextAttemptAt(Created, 1, Created.AddHours(24)).ShouldBeNull();
    }

    [Fact]
    public void Attempts_start_at_one() =>
        Should.Throw<ArgumentOutOfRangeException>(() => EmailRetryPolicy.NextAttemptAt(Created, 0, Created));
}
