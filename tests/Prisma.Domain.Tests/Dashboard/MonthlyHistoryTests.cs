using Prisma.Domain.Accounts;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.3 (docs/fase-2.md, 2.3): os 6 meses que terminam no escolhido, do mais antigo ao mais recente.
public sealed class MonthlyHistoryTests
{
    private static MonthlyEntry Expense(int year, int month, long cents) =>
        new(new DateOnly(year, month, 1), new SummaryEntry(TransactionType.Expense, null, AccountType.Checking, cents));

    private static MonthlyEntry Income(int year, int month, long cents) =>
        new(new DateOnly(year, month, 1), new SummaryEntry(TransactionType.Income, null, AccountType.Checking, cents));

    [Fact]
    public void Six_months_ending_in_the_chosen_one_with_zeros_for_empty_months()
    {
        var history = MonthlyHistory.Of(new DateOnly(2026, 10, 1), [Expense(2026, 10, 400000), Income(2026, 8, 800000)]);

        history.Select(m => m.Month).ShouldBe(
        [
            new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1),
        ]);
        history[3].Summary.IncomeCents.ShouldBe(800000);
        history[5].Summary.ExpenseCents.ShouldBe(400000);
        history[0].Summary.ShouldBe(new MonthlySummary(0, 0, 0, 0, 0));
        history[4].Summary.ShouldBe(new MonthlySummary(0, 0, 0, 0, 0));
    }

    [Fact]
    public void February_goes_back_to_September_of_the_previous_year()
    {
        var history = MonthlyHistory.Of(new DateOnly(2027, 2, 1), [Expense(2026, 12, 1000), Expense(2027, 1, 2000)]);

        history.Select(m => m.Month).ShouldBe(
        [
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1),
            new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1),
        ]);
        history[3].Summary.ExpenseCents.ShouldBe(1000);
        history[4].Summary.ExpenseCents.ShouldBe(2000);
    }

    [Fact]
    public void Entries_outside_the_window_are_ignored()
    {
        var history = MonthlyHistory.Of(new DateOnly(2026, 10, 1), [Expense(2026, 4, 999), Expense(2026, 11, 999)]);

        history.Sum(m => m.Summary.ExpenseCents).ShouldBe(0);
    }

    [Fact]
    public void Each_month_follows_the_summary_rules()
    {
        var october = new DateOnly(2026, 10, 1);
        var history = MonthlyHistory.Of(october,
        [
            Income(2026, 10, 800000),
            Expense(2026, 10, 400000),
            new MonthlyEntry(october, new SummaryEntry(TransactionType.Transfer, TransferDirection.In, AccountType.Investment, 150000)),
            new MonthlyEntry(october, new SummaryEntry(TransactionType.Transfer, TransferDirection.Out, AccountType.Checking, 150000)),
        ]);

        history[5].Summary.ShouldBe(new MonthlySummary(800000, 400000, 0, 400000, 150000));
    }

    [Fact]
    public void The_month_must_be_the_first_day()
    {
        Should.Throw<ArgumentException>(() => MonthlyHistory.Of(new DateOnly(2026, 10, 15), []));
    }
}
