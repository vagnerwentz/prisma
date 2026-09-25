using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// docs/fase-2.md, 2.5, regra 12: valor líquido por categoria raiz; o que fica zero ou negativo some,
// e o quanto de estorno sumiu vira o aviso da lista.
public sealed class CategoryTotalsTests
{
    private static readonly Guid Shopping = Guid.NewGuid(), Food = Guid.NewGuid(), Housing = Guid.NewGuid();

    private static CategoryAmount Expense(Guid? root, long cents) => new(root, TransactionType.Expense, cents);

    private static CategoryAmount Refund(Guid? root, long cents) => new(root, TransactionType.Refund, cents);

    [Fact]
    public void November_of_the_spec_example()
    {
        var totals = CategoryTotals.Of(
        [
            Expense(Shopping, 30000), Expense(Shopping, 20000), // sapato e fone
            Refund(Shopping, 4000), Refund(Shopping, 30000),    // estornos avulsos
            Refund(Food, 15000),                                // estorno do jantar
        ]);

        totals.Shown.ShouldBe([new CategoryNet(Shopping, 16000)]);
        totals.HiddenRefundCents.ShouldBe(15000);
        (totals.Shown.Sum(c => c.AmountCents) - totals.HiddenRefundCents).ShouldBe(1000); // o "Saiu" de novembro
    }

    [Fact]
    public void Without_refunds_every_category_with_expense_is_shown()
    {
        var totals = CategoryTotals.Of([Expense(Housing, 250000), Expense(Food, 80000), Expense(Food, 60000), Expense(null, 10000)]);

        totals.Shown.ShouldBe([new CategoryNet(Housing, 250000), new CategoryNet(Food, 140000), new CategoryNet(null, 10000)], ignoreOrder: true);
        totals.HiddenRefundCents.ShouldBe(0);
    }

    [Fact]
    public void A_category_that_nets_to_zero_disappears_without_a_notice()
    {
        var totals = CategoryTotals.Of([Expense(Food, 5000), Refund(Food, 5000), Expense(Housing, 1000)]);

        totals.Shown.ShouldBe([new CategoryNet(Housing, 1000)]);
        totals.HiddenRefundCents.ShouldBe(0);
    }

    [Fact]
    public void An_uncategorized_refund_nets_against_uncategorized_expenses()
    {
        var totals = CategoryTotals.Of([Expense(null, 10000), Refund(null, 3000)]);

        totals.Shown.ShouldBe([new CategoryNet(null, 7000)]);
    }

    [Fact]
    public void Only_expenses_and_refunds_are_accepted()
    {
        Should.Throw<ArgumentException>(() => CategoryTotals.Of([new CategoryAmount(Food, TransactionType.Income, 1)]));
    }
}
