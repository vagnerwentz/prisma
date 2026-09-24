using Prisma.Domain.Accounts;
using Shouldly;

namespace Prisma.Domain.Tests.Accounts;

public sealed class AccountTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Result<Account> CreditCard(int? closingDay = 5, int? dueDay = 12, long? limit = 500000) =>
        Account.Create(UserId, "Cartão Itaú Visa", AccountType.CreditCard, 0, closingDay, dueDay, limit);

    private static Account Checking() =>
        Account.Create(UserId, "Itaú Personnalité", AccountType.Checking, 150000, null, null, null).Value;

    private static void ShouldFailWith(Result<Account> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Credit_card_is_created_with_closing_and_due_day()
    {
        var account = CreditCard().Value;

        account.UserId.ShouldBe(UserId);
        account.Type.ShouldBe(AccountType.CreditCard);
        account.ClosingDay.ShouldBe(5);
        account.DueDay.ShouldBe(12);
        account.CreditLimitCents.ShouldBe(500000);
        account.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Credit_card_without_closing_day_cannot_be_created() =>
        ShouldFailWith(CreditCard(closingDay: null), "Cartão de crédito exige dia de fechamento.");

    [Fact]
    public void Credit_card_without_due_day_cannot_be_created() =>
        ShouldFailWith(CreditCard(dueDay: null), "Cartão de crédito exige dia de vencimento.");

    [Fact]
    public void Credit_card_limit_is_optional() =>
        CreditCard(limit: null).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Closing_day_must_be_between_1_and_31(int day) =>
        ShouldFailWith(CreditCard(closingDay: day), "O dia de fechamento deve estar entre 1 e 31.");

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Due_day_must_be_between_1_and_31(int day) =>
        ShouldFailWith(CreditCard(dueDay: day), "O dia de vencimento deve estar entre 1 e 31.");

    [Theory]
    [InlineData(1, 31)]
    [InlineData(31, 1)]
    public void Days_at_the_limits_are_accepted(int closingDay, int dueDay) =>
        CreditCard(closingDay, dueDay).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Credit_limit_cannot_be_negative() =>
        ShouldFailWith(CreditCard(limit: -1), "O limite do cartão não pode ser negativo.");

    [Theory]
    [InlineData(AccountType.Checking)]
    [InlineData(AccountType.Cash)]
    [InlineData(AccountType.Investment)]
    public void Other_types_cannot_have_closing_or_due_day(AccountType type)
    {
        const string message = "Apenas cartão de crédito tem dia de fechamento e de vencimento.";
        ShouldFailWith(Account.Create(UserId, "Conta", type, 0, 5, null, null), message);
        ShouldFailWith(Account.Create(UserId, "Conta", type, 0, null, 12, null), message);
    }

    [Theory]
    [InlineData(AccountType.Checking)]
    [InlineData(AccountType.Cash)]
    [InlineData(AccountType.Investment)]
    public void Other_types_cannot_have_credit_limit(AccountType type) =>
        ShouldFailWith(Account.Create(UserId, "Conta", type, 0, null, null, 100000),
            "Apenas cartão de crédito tem limite.");

    [Fact]
    public void Initial_balance_can_be_negative() =>
        Account.Create(UserId, "Conta com cheque especial", AccountType.Checking, -25000, null, null, null)
            .Value.InitialBalanceCents.ShouldBe(-25000);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Name_is_required(string? name) =>
        ShouldFailWith(Account.Create(UserId, name!, AccountType.Cash, 0, null, null, null),
            "Informe o nome da conta.");

    [Fact]
    public void Name_is_trimmed() =>
        Account.Create(UserId, "  Carteira  ", AccountType.Cash, 0, null, null, null)
            .Value.Name.ShouldBe("Carteira");

    [Fact]
    public void Name_has_at_most_100_characters()
    {
        Account.Create(UserId, new string('a', 100), AccountType.Cash, 0, null, null, null).IsSuccess.ShouldBeTrue();
        ShouldFailWith(Account.Create(UserId, new string('a', 101), AccountType.Cash, 0, null, null, null),
            "O nome da conta deve ter no máximo 100 caracteres.");
    }

    [Fact]
    public void Update_changes_the_editable_fields()
    {
        var account = CreditCard().Value;

        var result = account.Update("Cartão renomeado", -1000, 10, 20, 800000, isActive: false);

        result.IsSuccess.ShouldBeTrue();
        account.Name.ShouldBe("Cartão renomeado");
        account.InitialBalanceCents.ShouldBe(-1000);
        account.ClosingDay.ShouldBe(10);
        account.DueDay.ShouldBe(20);
        account.CreditLimitCents.ShouldBe(800000);
        account.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Update_keeps_the_invariants_and_leaves_the_account_untouched_on_failure()
    {
        var account = CreditCard().Value;

        var result = account.Update("Outro nome", 0, null, 12, null, isActive: true);

        ShouldFailWith(result, "Cartão de crédito exige dia de fechamento.");
        account.Name.ShouldBe("Cartão Itaú Visa");
        account.ClosingDay.ShouldBe(5);
    }

    [Fact]
    public void Update_of_other_types_rejects_card_fields() =>
        ShouldFailWith(Checking().Update("Conta", 0, 5, 12, null, isActive: true),
            "Apenas cartão de crédito tem dia de fechamento e de vencimento.");

    [Fact]
    public void Account_without_transactions_can_be_deleted() =>
        Checking().CheckCanDelete(activeTransactionCount: 0).ShouldBeNull();

    [Fact]
    public void Account_with_transactions_cannot_be_deleted()
    {
        var error = Checking().CheckCanDelete(activeTransactionCount: 1);

        error.ShouldNotBeNull();
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe(
            "Esta conta tem transações. Mova ou exclua as transações antes, ou marque a conta como inativa.");
    }
}
