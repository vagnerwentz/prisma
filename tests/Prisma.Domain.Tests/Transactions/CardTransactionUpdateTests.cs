using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/fase-1.md, 2.2: a parcela isolada muda só descrição e categoria; a compra à vista no
// cartão aceita também o valor. Conta, tipo, meio de pagamento e data nunca mudam.
public sealed class CardTransactionUpdateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Card = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static readonly Account OtherCard = Account.Create(UserId, "Master", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static readonly Category Electronics = Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;
    private static readonly Category Home = Category.Create(UserId, "Casa", TransactionType.Expense, null, null, null).Value;

    private static IReadOnlyList<Transaction> Buy(long total, int count) =>
        CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count, March10,
            Electronics, "Notebook", []).Value.Installments;

    private static Result<Transaction> Update(
        Transaction t, long? amount = null, Category? category = null, string? description = "Notebook",
        Account? account = null, TransactionType type = TransactionType.Expense,
        PaymentMethod method = PaymentMethod.Credit, DateOnly? date = null) =>
        t.Update(account ?? Card, type, amount ?? t.AmountCents, date ?? March10, category ?? Electronics, method, description);

    private static void ShouldFailWith(Result<Transaction> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Editing_installment_3_changes_only_its_description_and_category()
    {
        var installments = Buy(100000, 10);
        var third = installments[2];

        Update(third, category: Home, description: "Notebook (parcela renegociada)").IsSuccess.ShouldBeTrue();

        third.CategoryId.ShouldBe(Home.Id);
        third.Description.ShouldBe("Notebook (parcela renegociada)");
        third.AmountCents.ShouldBe(10000);
        installments.Where(t => t != third).ShouldAllBe(t => t.CategoryId == Electronics.Id && t.Description == "Notebook");
    }

    [Fact]
    public void Installment_amount_changes_through_the_whole_purchase() =>
        ShouldFailWith(Update(Buy(100000, 10)[2], amount: 12000),
            "O valor de uma parcela muda pela compra inteira, para a soma continuar igual ao total.");

    [Fact]
    public void Single_card_payment_accepts_a_new_amount_and_keeps_its_statement()
    {
        var single = Buy(4590, 1)[0];
        var statementId = single.StatementId;
        var settlement = single.SettlementDate;

        Update(single, amount: 4990).IsSuccess.ShouldBeTrue();

        single.AmountCents.ShouldBe(4990);
        single.StatementId.ShouldBe(statementId);
        single.SettlementDate.ShouldBe(settlement);
    }

    [Fact]
    public void Single_card_payment_amount_must_be_positive() =>
        ShouldFailWith(Update(Buy(4590, 1)[0], amount: 0), "O valor deve ser maior que zero.");

    [Fact]
    public void Account_type_method_and_date_never_change()
    {
        const string message = "Em compra no cartão, conta, tipo, meio de pagamento e data não mudam. Exclua e lance de novo.";
        var single = Buy(4590, 1)[0];

        ShouldFailWith(Update(single, account: OtherCard), message);
        ShouldFailWith(Update(single, date: March10.AddDays(1)), message);
        ShouldFailWith(Update(single, method: PaymentMethod.Pix), message);
        ShouldFailWith(Update(single, type: TransactionType.Income, category: null), message);
    }

    [Fact]
    public void Simple_transactions_keep_the_simple_rules()
    {
        var checking = Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
        var simple = Transaction.CreateSimple(UserId, checking, TransactionType.Expense, 4590, March10, null, PaymentMethod.Pix, null).Value;

        simple.Update(checking, TransactionType.Expense, 5000, March10.AddDays(2), null, PaymentMethod.Debit, "Café").IsSuccess.ShouldBeTrue();

        simple.AmountCents.ShouldBe(5000);
        simple.SettlementDate.ShouldBe(March10.AddDays(2));
    }
}
