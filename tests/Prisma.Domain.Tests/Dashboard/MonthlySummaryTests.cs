using Prisma.Domain.Accounts;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.1 (docs/fase-2.md, 2.1): receitas, despesas, sobra e investido de um mês.
public sealed class MonthlySummaryTests
{
    private static SummaryEntry Income(long cents, AccountType account = AccountType.Checking) =>
        new(TransactionType.Income, null, account, cents);

    private static SummaryEntry Expense(long cents, AccountType account = AccountType.Checking) =>
        new(TransactionType.Expense, null, account, cents);

    private static SummaryEntry TransferIn(AccountType account, long cents) =>
        new(TransactionType.Transfer, TransferDirection.In, account, cents);

    private static SummaryEntry TransferOut(AccountType account, long cents) =>
        new(TransactionType.Transfer, TransferDirection.Out, account, cents);

    [Fact]
    public void Empty_month_is_all_zeros()
    {
        MonthlySummary.Of([]).ShouldBe(new MonthlySummary(0, 0, 0, 0, 0));
    }

    // O exemplo da seção 2 de docs/fase-2.md, já somado por tipo, direção e tipo de conta.
    [Fact]
    public void Spec_example_october()
    {
        var summary = MonthlySummary.Of(
        [
            Income(800000),                                        // salário
            Expense(250000), Expense(80000), Expense(10000),       // aluguel, mercado, farmácia
            Expense(60000, AccountType.CreditCard),                // jantar, fatura que vence 05/10
            TransferOut(AccountType.Checking, 60000),              // pagamento da fatura
            TransferIn(AccountType.CreditCard, 60000),
            TransferOut(AccountType.Checking, 150000),             // aporte
            TransferIn(AccountType.Investment, 150000),
            TransferOut(AccountType.Investment, 50000),            // resgate
            TransferIn(AccountType.Checking, 50000),
            TransferOut(AccountType.Checking, 20000),              // saque
            TransferIn(AccountType.Cash, 20000),
        ]);

        summary.IncomeCents.ShouldBe(800000);
        summary.ExpenseCents.ShouldBe(400000);
        summary.CardExpenseCents.ShouldBe(60000);
        summary.LeftoverCents.ShouldBe(400000);
        summary.InvestedCents.ShouldBe(100000);
    }

    [Fact]
    public void Paying_the_statement_does_not_count_the_purchase_twice()
    {
        var summary = MonthlySummary.Of(
        [
            Expense(60000, AccountType.CreditCard),
            TransferOut(AccountType.Checking, 60000),
            TransferIn(AccountType.CreditCard, 60000),
        ]);

        summary.ExpenseCents.ShouldBe(60000);
        summary.CardExpenseCents.ShouldBe(60000);
        summary.IncomeCents.ShouldBe(0);
        summary.InvestedCents.ShouldBe(0);
    }

    [Fact]
    public void Transfers_between_investment_accounts_cancel_out()
    {
        var summary = MonthlySummary.Of([TransferOut(AccountType.Investment, 30000), TransferIn(AccountType.Investment, 30000)]);

        summary.InvestedCents.ShouldBe(0);
    }

    [Fact]
    public void Redeeming_more_than_investing_is_negative()
    {
        var summary = MonthlySummary.Of(
        [
            TransferIn(AccountType.Investment, 10000),
            TransferOut(AccountType.Investment, 40000),
        ]);

        summary.InvestedCents.ShouldBe(-30000);
    }

    [Fact]
    public void Leftover_can_be_negative_and_ignores_what_was_invested()
    {
        var summary = MonthlySummary.Of([Income(100000), Expense(150000), TransferIn(AccountType.Investment, 20000)]);

        summary.LeftoverCents.ShouldBe(-50000);
        summary.InvestedCents.ShouldBe(20000);
    }

    [Fact]
    public void Card_expense_is_the_part_of_the_expense_made_on_credit_cards()
    {
        var summary = MonthlySummary.Of(
        [
            Expense(10000),
            Expense(20000, AccountType.CreditCard),
            Expense(5000, AccountType.Cash),
            Income(3000, AccountType.CreditCard),
        ]);

        summary.ExpenseCents.ShouldBe(35000);
        summary.CardExpenseCents.ShouldBe(20000);
    }

    private static SummaryEntry Refund(long cents, AccountType account = AccountType.Checking) =>
        new(TransactionType.Refund, null, account, cents);

    // Exemplo da seção 2.5 de docs/fase-2.md: estorno abate despesa, nunca é receita.
    [Fact]
    public void Refunds_reduce_the_expense_of_the_month_and_never_count_as_income()
    {
        var october = MonthlySummary.Of(
        [
            Expense(60000, AccountType.CreditCard), // jantar, fatura que vence 05/10
            Expense(25000),                         // mercado no débito
            Refund(5000),                           // estorno do mercado
        ]);
        october.ShouldBe(new MonthlySummary(0, 80000, 60000, -80000, 0));

        var november = MonthlySummary.Of(
        [
            Expense(30000, AccountType.CreditCard), Expense(20000, AccountType.CreditCard), // sapato e fone
            Refund(15000, AccountType.CreditCard), Refund(4000, AccountType.CreditCard), Refund(30000, AccountType.CreditCard),
        ]);
        november.ShouldBe(new MonthlySummary(0, 1000, 1000, -1000, 0));
    }

    [Fact]
    public void A_month_with_only_refunds_has_negative_expense()
    {
        var summary = MonthlySummary.Of([Income(100000), Refund(5000, AccountType.CreditCard)]);

        summary.ShouldBe(new MonthlySummary(100000, -5000, -5000, 105000, 0));
    }

    [Fact]
    public void A_transfer_without_direction_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            MonthlySummary.Of([new SummaryEntry(TransactionType.Transfer, null, AccountType.Investment, 100)]));
    }
}
