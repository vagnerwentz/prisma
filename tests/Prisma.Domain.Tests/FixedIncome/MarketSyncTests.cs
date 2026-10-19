using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// A janela do reprocessamento manual e o registro de uma execução (docs/admin-mercado.md, 4, 5 e 6). Os limites vêm da
// especificação: de 1 dia a 10 anos, nunca no futuro, nunca antes do início do histórico.
public sealed class MarketSyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateOnly Earliest = new(2015, 1, 1);

    private static string? Check(DateOnly from, DateOnly to) => SyncWindow.Validate(from, to, Today, Earliest);

    [Fact]
    public void A_one_day_window_is_valid() => Check(Today, Today).ShouldBeNull();

    [Fact]
    public void The_window_may_start_on_the_first_day_of_the_history() => Check(Earliest, new DateOnly(2015, 1, 31)).ShouldBeNull();

    [Fact]
    public void The_start_cannot_be_after_the_end() => Check(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)).ShouldNotBeNull();

    [Fact]
    public void The_end_cannot_be_in_the_future() => Check(new DateOnly(2026, 10, 1), Today.AddDays(1)).ShouldNotBeNull();

    [Fact]
    public void The_start_cannot_be_before_the_history() => Check(Earliest.AddDays(-1), new DateOnly(2015, 1, 31)).ShouldNotBeNull();

    // Exatamente 10 anos vale; um dia a mais não.
    [Fact]
    public void Ten_years_is_the_ceiling()
    {
        Check(new DateOnly(2016, 5, 10), new DateOnly(2026, 5, 10)).ShouldBeNull();
        Check(new DateOnly(2016, 5, 10), new DateOnly(2026, 5, 11)).ShouldNotBeNull();
    }

    // Ano bissexto: 29/02/2016 + 10 anos cai em 28/02/2026 (o .NET leva ao último dia do mês); o teto acompanha o calendário.
    [Fact]
    public void The_ceiling_follows_the_calendar_in_leap_years() =>
        Check(new DateOnly(2016, 2, 29), new DateOnly(2026, 2, 28)).ShouldBeNull();

    [Fact]
    public void The_messages_are_in_portuguese() =>
        Check(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)).ShouldBe("A data inicial não pode ser depois da final.");

    private static readonly DateTime Start = new(2026, 10, 19, 22, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly From = new(2026, 10, 9);

    [Fact]
    public void A_successful_run_keeps_the_counts_and_has_no_error()
    {
        var run = MarketSyncRun.Succeeded(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start.AddSeconds(3), From, Today, 8, 2, 1, 5);

        (run.Fetched, run.Added, run.Revised, run.Unchanged).ShouldBe((8, 2, 1, 5));
        run.Outcome.ShouldBe(MarketSyncOutcome.Succeeded);
        run.ErrorSummary.ShouldBeNull();
        run.SourceErrorCode.ShouldBeNull();
    }

    [Theory]
    [InlineData(MarketSyncOutcome.SourceUnavailable, "O Banco Central não respondeu como esperado.")]
    [InlineData(MarketSyncOutcome.Failed, "A sincronização falhou por um erro interno.")]
    [InlineData(MarketSyncOutcome.TimedOut, "A sincronização passou do tempo limite.")]
    public void An_unsuccessful_run_has_a_fixed_summary_and_zero_counts(MarketSyncOutcome outcome, string summary)
    {
        var run = MarketSyncRun.Unsuccessful(ReferenceRateSeries.Cdi, MarketSyncKind.Scheduled, Start, Start, From, Today, outcome);

        run.ErrorSummary.ShouldBe(summary);
        (run.Fetched, run.Added, run.Revised, run.Unchanged).ShouldBe((0, 0, 0, 0));
        run.ErrorSummary!.Length.ShouldBeLessThanOrEqualTo(MarketSyncRun.MaxErrorSummaryLength);
    }

    [Fact]
    public void The_source_error_code_is_kept_only_for_a_source_failure()
    {
        var run = MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.SourceUnavailable, "7240913300019");

        run.SourceErrorCode.ShouldBe("7240913300019");
        Should.Throw<ArgumentException>(() => MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.Failed, "123"));
    }

    // Só dígitos e até 40: nada do corpo da resposta passa pelo código.
    [Theory]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("abc")]
    [InlineData("12<script>")]
    public void The_source_error_code_is_only_digits(string code) =>
        Should.Throw<ArgumentException>(() => MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.SourceUnavailable, code));

    [Fact]
    public void The_source_error_code_has_at_most_forty_digits()
    {
        MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.SourceUnavailable, new string('7', 40));

        Should.Throw<ArgumentException>(() => MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.SourceUnavailable, new string('7', 41)));
    }

    [Fact]
    public void A_run_that_succeeded_cannot_be_built_as_unsuccessful() =>
        Should.Throw<ArgumentException>(() => MarketSyncRun.Unsuccessful(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, MarketSyncOutcome.Succeeded));

    [Fact]
    public void A_run_does_not_finish_before_it_starts() =>
        Should.Throw<ArgumentException>(() => MarketSyncRun.Succeeded(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start.AddSeconds(-1), From, Today, 0, 0, 0, 0));

    [Fact]
    public void A_run_does_not_have_a_backwards_window() =>
        Should.Throw<ArgumentException>(() => MarketSyncRun.Succeeded(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, Today, From, 0, 0, 0, 0));

    [Fact]
    public void Counts_are_never_negative() =>
        Should.Throw<ArgumentOutOfRangeException>(() => MarketSyncRun.Succeeded(
            ReferenceRateSeries.Cdi, MarketSyncKind.Manual, Start, Start, From, Today, 1, -1, 0, 0));
}
