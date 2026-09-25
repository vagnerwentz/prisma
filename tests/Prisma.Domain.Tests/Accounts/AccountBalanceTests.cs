using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Accounts;

// Etapa 1.16 (docs/fase-1.md, 2.5): saldo atual até hoje, previsto com o que vem depois.
public sealed class AccountBalanceTests
{
    private static BalanceEntry Settled(TransactionType type, long cents, TransferDirection? direction = null) =>
        new(type, direction, cents, IsSettled: true);

    private static BalanceEntry Future(TransactionType type, long cents, TransferDirection? direction = null) =>
        new(type, direction, cents, IsSettled: false);

    [Fact]
    public void Without_transactions_both_balances_are_the_initial_balance()
    {
        var balance = AccountBalance.Of(100000, []);

        balance.ShouldBe(new AccountBalance(100000, 100000));
    }

    [Theory]
    [InlineData(TransactionType.Income, null, 5000)]
    [InlineData(TransactionType.Expense, null, -5000)]
    [InlineData(TransactionType.Transfer, TransferDirection.In, 5000)]
    [InlineData(TransactionType.Transfer, TransferDirection.Out, -5000)]
    public void Each_kind_of_transaction_moves_the_balance_in_its_direction(
        TransactionType type, TransferDirection? direction, long expectedChange)
    {
        var balance = AccountBalance.Of(0, [Settled(type, 5000, direction)]);

        balance.CurrentCents.ShouldBe(expectedChange);
        balance.ProjectedCents.ShouldBe(expectedChange);
    }

    [Fact]
    public void A_transfer_without_direction_is_rejected()
    {
        Should.Throw<ArgumentException>(() => AccountBalance.Of(0, [Settled(TransactionType.Transfer, 5000)]));
    }

    // O exemplo da regra 2.5.
    [Fact]
    public void Spec_example_current_and_projected_balance()
    {
        var balance = AccountBalance.Of(100000,
        [
            Settled(TransactionType.Income, 300000),                            // salário
            Settled(TransactionType.Expense, 4590),                             // Pix
            Settled(TransactionType.Transfer, 20000, TransferDirection.Out),    // para a carteira
            Settled(TransactionType.Transfer, 10000, TransferDirection.Out),    // fatura paga
            Future(TransactionType.Expense, 7000),                              // semana que vem
        ]);

        balance.CurrentCents.ShouldBe(365410);
        balance.ProjectedCents.ShouldBe(358410);
    }

    [Fact]
    public void Future_transactions_only_change_the_projected_balance()
    {
        var balance = AccountBalance.Of(1000, [Future(TransactionType.Income, 500), Future(TransactionType.Expense, 200)]);

        balance.CurrentCents.ShouldBe(1000);
        balance.ProjectedCents.ShouldBe(1300);
    }

    [Fact]
    public void Balance_can_go_negative()
    {
        AccountBalance.Of(1000, [Settled(TransactionType.Expense, 1500)]).CurrentCents.ShouldBe(-500);
    }

    [Theory]
    [InlineData(500000L, 30000L, 470000L)]
    [InlineData(500000L, 0L, 500000L)]
    [InlineData(20000L, 30000L, -10000L)]
    public void Available_credit_is_the_limit_minus_what_is_owed(long limit, long owed, long expected)
    {
        CreditCardBalance.Of(limit, owed).ShouldBe(new CreditCardBalance(owed, expected));
    }

    [Fact]
    public void Without_a_limit_there_is_no_available_credit()
    {
        CreditCardBalance.Of(null, 30000).ShouldBe(new CreditCardBalance(30000, null));
    }
}
