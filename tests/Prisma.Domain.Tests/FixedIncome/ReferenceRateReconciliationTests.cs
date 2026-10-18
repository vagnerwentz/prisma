using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O que a sincronização faz com as taxas que chegam, contra as que já estão gravadas (docs/renda-fixa.md, 3.2 e 3.3): data
// nova entra, igual não faz nada, valor diferente é revisão explícita. Nunca apaga.
public sealed class ReferenceRateReconciliationTests
{
    private static readonly DateTime Day1 = new(2026, 10, 16, 22, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);
    private const string Source = "bcb-sgs-12";

    private static DailyRate Rate(int day, long rateE8 = 50_788) => new(new DateOnly(2026, 10, day), rateE8);

    private static ReferenceRate Stored(int day, long rateE8 = 50_788) =>
        ReferenceRate.From(ReferenceRateSeries.Cdi, Rate(day, rateE8), Source, Day1);

    private static ReferenceRateChanges Apply(IEnumerable<ReferenceRate> existing, params DailyRate[] incoming) =>
        ReferenceRateReconciliation.Apply(ReferenceRateSeries.Cdi, existing, incoming, Source, Day2);

    [Fact]
    public void New_dates_are_added_with_their_origin_and_moment()
    {
        var changes = Apply([], Rate(13), Rate(14));

        changes.Added.Select(r => (r.ReferenceDate, r.DailyRateE8, r.Source, r.FetchedAt, r.RevisedAt))
            .ShouldBe([
                (new DateOnly(2026, 10, 13), 50_788, Source, Day2, (DateTime?)null),
                (new DateOnly(2026, 10, 14), 50_788, Source, Day2, (DateTime?)null),
            ]);
        changes.Revised.ShouldBeEmpty();
        changes.Unchanged.ShouldBe(0);
        changes.HasChanges.ShouldBeTrue();
    }

    [Fact]
    public void The_same_values_change_nothing_and_leave_the_stored_rows_alone()
    {
        var stored = new[] { Stored(13), Stored(14) };

        var changes = Apply(stored, Rate(13), Rate(14));

        changes.Added.ShouldBeEmpty();
        changes.Revised.ShouldBeEmpty();
        changes.Unchanged.ShouldBe(2);
        changes.HasChanges.ShouldBeFalse();
        stored.ShouldAllBe(r => r.FetchedAt == Day1 && r.RevisedAt == null);
    }

    // A fonte corrigiu um valor já gravado: o número muda, o instante da revisão fica anotado, e o resto continua.
    [Fact]
    public void A_different_value_for_a_stored_date_is_a_revision()
    {
        var stored = new[] { Stored(13), Stored(14) };

        var changes = Apply(stored, Rate(13), Rate(14, 51_000));

        changes.Revised.ShouldBe([new DateOnly(2026, 10, 14)]);
        changes.Unchanged.ShouldBe(1);
        stored[1].DailyRateE8.ShouldBe(51_000);
        stored[1].RevisedAt.ShouldBe(Day2);
        stored[1].FetchedAt.ShouldBe(Day2);
        stored[0].DailyRateE8.ShouldBe(50_788);
        stored[0].RevisedAt.ShouldBeNull();
        stored[0].FetchedAt.ShouldBe(Day1);
    }

    [Fact]
    public void New_revised_and_unchanged_dates_in_the_same_batch()
    {
        var stored = new[] { Stored(13), Stored(14) };

        var changes = Apply(stored, Rate(13, 49_000), Rate(14), Rate(15));

        (changes.Added.Count, changes.Revised.Count, changes.Unchanged).ShouldBe((1, 1, 1));
        changes.Added.Single().ReferenceDate.ShouldBe(new DateOnly(2026, 10, 15));
        changes.Revised.Single().ShouldBe(new DateOnly(2026, 10, 13));
    }

    // Nunca apaga: o que está gravado e não veio na janela fica como está (a janela só olha para trás alguns dias).
    [Fact]
    public void A_stored_date_missing_from_the_incoming_batch_is_left_alone()
    {
        var stored = new[] { Stored(13), Stored(14) };

        var changes = Apply(stored, Rate(14));

        changes.Added.ShouldBeEmpty();
        changes.Revised.ShouldBeEmpty();
        changes.Unchanged.ShouldBe(1);
        stored.Select(r => r.ReferenceDate.Day).ShouldBe([13, 14]);
    }

    [Fact]
    public void An_empty_batch_changes_nothing()
    {
        var stored = new[] { Stored(13) };

        var changes = Apply(stored);

        (changes.Added.Count, changes.Revised.Count, changes.Unchanged, changes.HasChanges).ShouldBe((0, 0, 0, false));
    }

    // O que garante a idempotência: aplicar o mesmo lote duas vezes dá o mesmo estado, e a segunda não faz nada.
    [Fact]
    public void Applying_the_same_batch_twice_changes_nothing_the_second_time()
    {
        var batch = new[] { Rate(13), Rate(14), Rate(15, 52_000) };

        var first = Apply([], batch);
        var second = Apply(first.Added, batch);

        second.HasChanges.ShouldBeFalse();
        second.Unchanged.ShouldBe(3);
    }

    [Fact]
    public void A_repeated_date_in_the_batch_is_a_programming_error() =>
        Should.Throw<ArgumentException>(() => Apply([], Rate(13), Rate(13)));

    [Fact]
    public void A_repeated_date_among_the_stored_rows_is_a_programming_error() =>
        Should.Throw<ArgumentException>(() => Apply([Stored(13), Stored(13)], Rate(13)));

    [Fact]
    public void Revising_a_rate_with_another_date_is_refused() =>
        Should.Throw<ArgumentException>(() => Stored(13).Revise(Rate(14, 1), Day2));

    [Fact]
    public void Revising_with_the_same_value_returns_false()
    {
        var stored = Stored(13);

        stored.Revise(Rate(13), Day2).ShouldBeFalse();
        stored.RevisedAt.ShouldBeNull();
    }
}
