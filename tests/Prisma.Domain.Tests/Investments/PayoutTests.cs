using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Investments;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/investimentos.md, etapa 5b: o provento é um lançamento de receita ligado ao ativo.
public sealed class PayoutTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid Bbas3 = Guid.CreateVersion7();
    private static readonly DateOnly PaidOn = new(2026, 9, 30);

    private static Account Itau() => Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
    private static Account Ion() => Account.Create(UserId, "Íon", AccountType.Investment, 0, null, null, null).Value;
    private static Account Visa() => Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, 500000).Value;
    private static Category Income() => Category.Create(UserId, "Rendimentos", TransactionType.Income, null, null, null).Value;
    private static Category Expense() => Category.Create(UserId, "Mercado", TransactionType.Expense, null, null, null).Value;

    [Fact]
    public void Payout_is_income_in_the_account_on_the_payment_day()
    {
        var account = Ion();
        var category = Income();

        var payout = Transaction.CreatePayout(UserId, account, Bbas3, PayoutKind.InterestOnEquity, 3579, PaidOn, category).Value;

        payout.Type.ShouldBe(TransactionType.Income);
        payout.AccountId.ShouldBe(account.Id);
        payout.AmountCents.ShouldBe(3579);
        (payout.PurchaseDate, payout.SettlementDate).ShouldBe((PaidOn, PaidOn));
        (payout.AssetId, payout.PayoutKind).ShouldBe((Bbas3, PayoutKind.InterestOnEquity));
        payout.CategoryId.ShouldBe(category.Id);
        payout.Description.ShouldBe("");
        payout.Method.ShouldBe(PaymentMethod.Ted);
    }

    [Fact]
    public void Payout_can_land_in_a_checking_account() =>
        Transaction.CreatePayout(UserId, Itau(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, null).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Payout_never_lands_on_a_credit_card() =>
        Transaction.CreatePayout(UserId, Visa(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, null).Error!.Message
            .ShouldBe("Provento cai numa conta, não no cartão de crédito.");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Payout_needs_a_positive_amount(long cents) =>
        Transaction.CreatePayout(UserId, Ion(), Bbas3, PayoutKind.Dividend, cents, PaidOn, null).IsSuccess.ShouldBeFalse();

    [Fact]
    public void Payout_kind_must_be_known() =>
        Transaction.CreatePayout(UserId, Ion(), Bbas3, (PayoutKind)99, 1000, PaidOn, null).Error!.Message
            .ShouldBe("Tipo de provento inválido.");

    [Fact]
    public void Payout_category_must_be_income() =>
        Transaction.CreatePayout(UserId, Ion(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, Expense()).IsSuccess.ShouldBeFalse();

    [Fact]
    public void Editing_a_payout_changes_everything_and_keeps_it_a_payout()
    {
        var payout = Transaction.CreatePayout(UserId, Ion(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, null).Value;
        var itau = Itau();
        var mxrf = Guid.CreateVersion7();

        payout.UpdatePayout(itau, mxrf, PayoutKind.FundIncome, 980, PaidOn.AddDays(1), null).IsSuccess.ShouldBeTrue();

        (payout.AccountId, payout.AssetId, payout.PayoutKind, payout.AmountCents).ShouldBe((itau.Id, mxrf, PayoutKind.FundIncome, 980L));
        (payout.PurchaseDate, payout.SettlementDate).ShouldBe((PaidOn.AddDays(1), PaidOn.AddDays(1)));
        payout.Type.ShouldBe(TransactionType.Income);
    }

    [Fact]
    public void Common_edit_refuses_a_payout()
    {
        var payout = Transaction.CreatePayout(UserId, Ion(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, null).Value;

        var result = payout.UpdateSimple(Ion(), TransactionType.Expense, 1000, PaidOn, null, PaymentMethod.Pix, "x");

        result.Error!.Message.ShouldBe(Transaction.PayoutUsesItsOwnEdit);
        payout.AssetId.ShouldBe(Bbas3);
    }

    [Fact]
    public void Payout_edit_refuses_a_common_income()
    {
        var salary = Transaction.CreateSimple(UserId, Itau(), TransactionType.Income, 500000, PaidOn, null, PaymentMethod.Pix, "Salário").Value;

        salary.UpdatePayout(Itau(), Bbas3, PayoutKind.Dividend, 1000, PaidOn, null).IsSuccess.ShouldBeFalse();
        salary.AssetId.ShouldBeNull();
    }

    // A chave que o provento procura é a que o catálogo grava.
    [Fact]
    public void Investment_income_key_matches_the_catalog() =>
        DefaultCategories.CreateFor(UserId)
            .ShouldContain(c => c.TemplateKey == DefaultCategories.InvestmentIncomeKey && c.Type == TransactionType.Income);

    [Fact]
    public void Payout_does_not_repeat()
    {
        var account = Ion();
        var payout = Transaction.CreatePayout(UserId, account, Bbas3, PayoutKind.FundIncome, 980, PaidOn, null).Value;

        Recurrence.StartFrom(payout, account, RecurrenceFrequency.Monthly, null).Error!.Message.ShouldBe("Provento não se repete.");
    }
}
