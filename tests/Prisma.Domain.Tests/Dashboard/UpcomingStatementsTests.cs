using Prisma.Domain.Dashboard;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.4 (docs/fase-2.md, 2.4): a próxima fatura não paga de cada cartão, hoje 15/10/2026.
public sealed class UpcomingStatementsTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static readonly Guid Visa = Guid.NewGuid(), Master = Guid.NewGuid(), Elo = Guid.NewGuid(), Nubank = Guid.NewGuid();

    private static CardStatement Statement(Guid card, string name, string due, long cents, bool paid = false) =>
        new(card, name, Guid.NewGuid(), DateOnly.Parse(due), paid, cents);

    [Fact]
    public void Spec_example()
    {
        var upcoming = UpcomingStatements.Of(Today,
        [
            Statement(Visa, "Visa", "2026-10-05", 60000, paid: true),
            Statement(Visa, "Visa", "2026-11-05", 30000),
            Statement(Master, "Master", "2026-10-20", 10000, paid: true),
            Statement(Master, "Master", "2026-11-20", 10000),
            Statement(Master, "Master", "2026-12-20", 10000),
            Statement(Elo, "Elo", "2026-09-15", 5000),
            Statement(Elo, "Elo", "2026-10-15", 8000),
            Statement(Nubank, "Nubank", "2026-10-20", 0),
            Statement(Nubank, "Nubank", "2026-11-20", 4000),
        ]);

        upcoming.Statements.Select(s => (s.CardName, s.DueDate, s.TotalCents)).ShouldBe(
        [
            ("Elo", new DateOnly(2026, 10, 15), 8000L),
            ("Visa", new DateOnly(2026, 11, 5), 30000L),
            ("Master", new DateOnly(2026, 11, 20), 10000L),
            ("Nubank", new DateOnly(2026, 11, 20), 4000L),
        ]);
        upcoming.TotalCents.ShouldBe(52000);
    }

    [Fact]
    public void Due_today_counts_and_due_yesterday_does_not()
    {
        UpcomingStatements.Of(Today, [Statement(Elo, "Elo", "2026-10-15", 100)]).Statements.Count.ShouldBe(1);
        UpcomingStatements.Of(Today, [Statement(Elo, "Elo", "2026-10-14", 100)]).Statements.ShouldBeEmpty();
    }

    [Fact]
    public void Paid_zeroed_and_negative_statements_are_skipped_for_the_next_one()
    {
        var upcoming = UpcomingStatements.Of(Today,
        [
            Statement(Visa, "Visa", "2026-11-05", 30000, paid: true),
            Statement(Visa, "Visa", "2026-12-05", 0),
            Statement(Visa, "Visa", "2027-01-05", -2000),
            Statement(Visa, "Visa", "2027-02-05", 1500),
        ]);

        upcoming.Statements.Single().DueDate.ShouldBe(new DateOnly(2027, 2, 5));
        upcoming.TotalCents.ShouldBe(1500);
    }

    [Fact]
    public void Only_the_first_statement_of_each_card_counts_regardless_of_input_order()
    {
        var upcoming = UpcomingStatements.Of(Today,
        [
            Statement(Visa, "Visa", "2027-01-05", 999),
            Statement(Visa, "Visa", "2026-11-05", 100),
            Statement(Visa, "Visa", "2026-12-05", 999),
        ]);

        upcoming.Statements.Single().TotalCents.ShouldBe(100);
    }

    [Fact]
    public void Nothing_to_pay_is_empty_with_zero_total()
    {
        var upcoming = UpcomingStatements.Of(Today, [Statement(Visa, "Visa", "2026-11-05", 30000, paid: true)]);

        upcoming.Statements.ShouldBeEmpty();
        upcoming.TotalCents.ShouldBe(0);
    }
}
