using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

public sealed class TransactionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Checking =
        Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;

    private static readonly Account Card =
        Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;

    private static readonly Category Food =
        Category.Create(UserId, "Alimentação", TransactionType.Expense, null, null, null).Value;

    private static readonly Category Market =
        Category.Create(UserId, "Mercado", TransactionType.Expense, Food, null, null).Value;

    private static readonly Category Salary =
        Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    private static Result<Transaction> Expense(
        long amountCents = 4590, Account? account = null, Category? category = null,
        PaymentMethod method = PaymentMethod.Pix, string? description = "Padaria") =>
        Transaction.CreateSimple(UserId, account ?? Checking, TransactionType.Expense, amountCents, March10,
            category ?? Market, method, description);

    private static void ShouldFailWith(Result<Transaction> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Simple_expense_settles_on_the_purchase_date()
    {
        var transaction = Expense().Value;

        transaction.UserId.ShouldBe(UserId);
        transaction.AccountId.ShouldBe(Checking.Id);
        transaction.Type.ShouldBe(TransactionType.Expense);
        transaction.AmountCents.ShouldBe(4590);
        transaction.PurchaseDate.ShouldBe(March10);
        transaction.SettlementDate.ShouldBe(March10);
        transaction.CategoryId.ShouldBe(Market.Id);
        transaction.Method.ShouldBe(PaymentMethod.Pix);
        transaction.Description.ShouldBe("Padaria");
        transaction.Source.ShouldBe(TransactionSource.Manual);
        transaction.StatementId.ShouldBeNull();
        transaction.RawDescription.ShouldBeNull();
    }

    [Fact]
    public void Simple_income_with_income_category()
    {
        var transaction = Transaction.CreateSimple(UserId, Checking, TransactionType.Income, 850000, March10,
            Salary, PaymentMethod.Ted, "Salário de março").Value;

        transaction.Type.ShouldBe(TransactionType.Income);
        transaction.SettlementDate.ShouldBe(March10);
    }

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.Debit)]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Boleto)]
    [InlineData(PaymentMethod.Ted)]
    public void Accepts_every_non_credit_method(PaymentMethod method) =>
        Expense(method: method).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Category_is_optional() =>
        Transaction.CreateSimple(UserId, Checking, TransactionType.Expense, 100, March10, null, PaymentMethod.Pix, null)
            .Value.CategoryId.ShouldBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Amount_must_be_positive(long amountCents) =>
        ShouldFailWith(Expense(amountCents: amountCents), "O valor deve ser maior que zero.");

    [Fact]
    public void Transfer_is_not_a_simple_transaction() =>
        ShouldFailWith(
            Transaction.CreateSimple(UserId, Checking, TransactionType.Transfer, 100, March10, null, PaymentMethod.Pix, null),
            "Transferência entre contas usa a operação de transferência.");

    [Fact]
    public void Credit_card_account_is_not_a_simple_transaction() =>
        ShouldFailWith(Expense(account: Card),
            "Lançamento em cartão de crédito usa a compra no cartão.");

    [Fact]
    public void Credit_method_is_not_a_simple_transaction() =>
        ShouldFailWith(Expense(method: PaymentMethod.Credit),
            "Pagamento no crédito exige uma conta de cartão de crédito.");

    [Fact]
    public void Category_must_match_the_transaction_type() =>
        ShouldFailWith(Expense(category: Salary),
            "A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

    [Fact]
    public void Description_is_optional_and_trimmed()
    {
        Expense(description: null).Value.Description.ShouldBe("");
        Expense(description: "  Café  ").Value.Description.ShouldBe("Café");
    }

    [Fact]
    public void Description_has_at_most_200_characters()
    {
        Expense(description: new string('d', 200)).IsSuccess.ShouldBeTrue();
        ShouldFailWith(Expense(description: new string('d', 201)),
            "A descrição deve ter no máximo 200 caracteres.");
    }

    [Fact]
    public void Update_changes_the_fields_and_settlement_follows_the_purchase_date()
    {
        var transaction = Expense().Value;
        var newDate = new DateOnly(2026, 4, 2);

        var result = transaction.UpdateSimple(Checking, TransactionType.Income, 12000, newDate, Salary,
            PaymentMethod.Ted, "Reembolso");

        result.IsSuccess.ShouldBeTrue();
        transaction.Type.ShouldBe(TransactionType.Income);
        transaction.AmountCents.ShouldBe(12000);
        transaction.PurchaseDate.ShouldBe(newDate);
        transaction.SettlementDate.ShouldBe(newDate);
        transaction.CategoryId.ShouldBe(Salary.Id);
        transaction.Method.ShouldBe(PaymentMethod.Ted);
        transaction.Description.ShouldBe("Reembolso");
    }

    [Fact]
    public void Failed_update_leaves_the_transaction_untouched()
    {
        var transaction = Expense().Value;

        var result = transaction.UpdateSimple(Checking, TransactionType.Expense, 0, new DateOnly(2026, 4, 2),
            Market, PaymentMethod.Pix, "Outra");

        ShouldFailWith(result, "O valor deve ser maior que zero.");
        transaction.AmountCents.ShouldBe(4590);
        transaction.PurchaseDate.ShouldBe(March10);
        transaction.Description.ShouldBe("Padaria");
    }

    [Fact]
    public void Update_keeps_the_same_rules_as_creation() =>
        ShouldFailWith(
            Expense().Value.UpdateSimple(Card, TransactionType.Expense, 100, March10, null, PaymentMethod.Pix, null),
            "Lançamento em cartão de crédito usa a compra no cartão.");

    [Fact]
    public void Restore_keeps_the_category_when_it_still_exists()
    {
        var transaction = Expense().Value;

        transaction.Restore(categoryStillExists: true);

        transaction.CategoryId.ShouldBe(Market.Id);
        transaction.DeletedAt.ShouldBeNull();
    }

    // A categoria pode ter sido excluída enquanto a transação estava excluída (o bloqueio
    // só considera transações ativas). Restaurar a deixa sem categoria em vez de impedir.
    [Fact]
    public void Restore_clears_a_category_that_was_deleted_meanwhile()
    {
        var transaction = Expense().Value;

        transaction.Restore(categoryStillExists: false);

        transaction.CategoryId.ShouldBeNull();
    }
}
