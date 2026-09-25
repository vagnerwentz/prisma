using Prisma.Domain.Accounts;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.8 (docs/fase-2.md, 2.6): os 6 meses seguintes ao de hoje, com o "Saiu" já lançado.
public sealed class CommittedMonthsTests
{
    private static readonly DateOnly October15 = new(2026, 10, 15);

    private static MonthlyEntry Entry(int year, int month, TransactionType type, long cents, AccountType account = AccountType.CreditCard) =>
        new(new DateOnly(year, month, 1), new SummaryEntry(type, null, account, cents));

    private static MonthlyEntry Expense(int year, int month, long cents) => Entry(year, month, TransactionType.Expense, cents);

    [Fact]
    public void Spec_example_november_to_april_with_the_refund_and_the_last_installment()
    {
        MonthlyEntry[] entries =
        [
            // Novembro: TV, Passagem, Mercado 3/3, Tênis 2/3 e iFood, menos o estorno.
            Expense(2026, 11, 40000 + 60000 + 10000 + 20000 + 8000),
            Entry(2026, 11, TransactionType.Refund, 5000),
            Expense(2026, 12, 40000 + 60000 + 20000),
            Expense(2027, 1, 40000 + 60000),
            Expense(2027, 2, 40000 + 60000),
            Expense(2027, 3, 40000),
            Expense(2027, 4, 40000),
        ];

        var committed = CommittedMonths.Of(October15, entries, lastInstallmentDue: new DateOnly(2027, 4, 5));

        committed.Months.Select(m => (m.Month, m.ExpenseCents)).ShouldBe(
        [
            (new DateOnly(2026, 11, 1), 133000L),
            (new DateOnly(2026, 12, 1), 120000L),
            (new DateOnly(2027, 1, 1), 100000L),
            (new DateOnly(2027, 2, 1), 100000L),
            (new DateOnly(2027, 3, 1), 40000L),
            (new DateOnly(2027, 4, 1), 40000L),
        ]);
        committed.LastInstallmentMonth.ShouldBe(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void Empty_months_are_zero_and_transfers_income_and_other_months_do_not_count()
    {
        MonthlyEntry[] entries =
        [
            Expense(2026, 12, 30000),
            Entry(2026, 12, TransactionType.Income, 800000, AccountType.Checking),
            new(new DateOnly(2026, 12, 1), new SummaryEntry(TransactionType.Transfer, TransferDirection.In, AccountType.CreditCard, 30000)),
            Expense(2026, 10, 99900),
            Expense(2027, 5, 99900),
        ];

        var committed = CommittedMonths.Of(October15, entries, lastInstallmentDue: null);

        committed.Months.Select(m => m.ExpenseCents).ShouldBe([0L, 30000L, 0L, 0L, 0L, 0L]);
        committed.LastInstallmentMonth.ShouldBeNull();
    }

    [Fact]
    public void The_last_installment_may_be_beyond_the_six_months_and_the_window_crosses_the_year()
    {
        var committed = CommittedMonths.Of(new DateOnly(2026, 12, 31), [], lastInstallmentDue: new DateOnly(2028, 2, 20));

        committed.Months.Select(m => m.Month).ShouldBe(
        [
            new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 1),
            new DateOnly(2027, 4, 1), new DateOnly(2027, 5, 1), new DateOnly(2027, 6, 1),
        ]);
        committed.LastInstallmentMonth.ShouldBe(new DateOnly(2028, 2, 1));
    }

    [Fact]
    public void An_installment_due_this_month_is_not_a_future_one()
    {
        CommittedMonths.Of(October15, [], lastInstallmentDue: new DateOnly(2026, 10, 25)).LastInstallmentMonth.ShouldBeNull();
    }
}
