using Prisma.Domain.Accounts;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O aporte que sai de uma conta (docs/renda-fixa.md, D3): vira uma transferência para uma conta de investimento, então o saldo
// da conta cai e o "Investido" do Resumo sobe, sem virar receita nem despesa.
public sealed class FixedIncomeFundingTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 21);

    private static Account Make(string name, AccountType type) =>
        type == AccountType.CreditCard
            ? Account.Create(User, name, type, 0, 26, 5, null).Value
            : Account.Create(User, name, type, 0, null, null, null).Value;

    private static readonly Account Checking = Make("Itaú", AccountType.Checking);
    private static readonly Account Cash = Make("Carteira", AccountType.Cash);
    private static readonly Account Investment = Make("Itaú Investimentos", AccountType.Investment);
    private static readonly Account OtherInvestment = Make("Íon", AccountType.Investment);
    private static readonly Account Card = Make("Visa", AccountType.CreditCard);

    private static Result<TransferLegs> Create(Account from, Account to, long cents = 1_000_000, PaymentMethod method = PaymentMethod.Pix) =>
        FixedIncomeFunding.Create(User, from, to, cents, Day, method);

    [Fact]
    public void The_money_leaves_the_account_and_enters_the_investment_account_as_one_transfer()
    {
        var legs = Create(Checking, Investment, 1_000_000).Value;

        legs.Out.AccountId.ShouldBe(Checking.Id);
        legs.In.AccountId.ShouldBe(Investment.Id);
        legs.Out.TransferPairId.ShouldNotBeNull();
        legs.In.TransferPairId.ShouldBe(legs.Out.TransferPairId);
        (legs.Out.TransferDirection, legs.In.TransferDirection).ShouldBe((TransferDirection.Out, TransferDirection.In));
        (legs.Out.AmountCents, legs.In.AmountCents).ShouldBe((1_000_000L, 1_000_000L));
        (legs.Out.PurchaseDate, legs.In.PurchaseDate).ShouldBe((Day, Day));
        (legs.Out.SettlementDate, legs.In.SettlementDate).ShouldBe((Day, Day));
        legs.Out.Description.ShouldBe(FixedIncomeFunding.Description);
    }

    // Transferência nunca é receita nem despesa: não duplica o gasto do mês (CLAUDE.md, regra 5).
    [Fact]
    public void It_is_a_transfer_never_income_or_expense()
    {
        var legs = Create(Checking, Investment).Value;

        legs.Out.Type.ShouldBe(TransactionType.Transfer);
        legs.In.Type.ShouldBe(TransactionType.Transfer);
    }

    [Fact]
    public void The_wallet_can_fund_an_investment_too() => Create(Cash, Investment).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.Ted)]
    [InlineData(PaymentMethod.Boleto)]
    [InlineData(PaymentMethod.Debit)]
    [InlineData(PaymentMethod.Cash)]
    public void Any_method_but_credit_is_accepted(PaymentMethod method) => Create(Checking, Investment, method: method).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Credit_is_not_a_method_for_a_transfer() => Create(Checking, Investment, method: PaymentMethod.Credit).IsSuccess.ShouldBeFalse();

    [Fact]
    public void The_destination_must_be_an_investment_account()
    {
        var toChecking = Create(Cash, Checking);

        toChecking.IsSuccess.ShouldBeFalse();
        toChecking.Error!.Code.ShouldBe("FundingNeedsInvestmentAccount");
        toChecking.Error.Type.ShouldBe(ErrorType.Validation);
        Create(Checking, Card).Error!.Code.ShouldBe("FundingNeedsInvestmentAccount");
        Create(Checking, Cash).Error!.Code.ShouldBe("FundingNeedsInvestmentAccount");
    }

    [Fact]
    public void The_money_comes_from_a_checking_account_or_the_wallet_not_a_card_nor_another_investment()
    {
        var fromCard = Create(Card, Investment);
        var fromInvestment = Create(OtherInvestment, Investment);

        fromCard.Error!.Code.ShouldBe("FundingNeedsPaymentAccount");
        fromInvestment.Error!.Code.ShouldBe("FundingNeedsPaymentAccount");
        fromInvestment.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void The_amount_must_be_positive(long cents) => Create(Checking, Investment, cents).IsSuccess.ShouldBeFalse();

    // Cada chamada é uma transferência nova: dois aportes nunca dividem o mesmo par.
    [Fact]
    public void Two_fundings_never_share_the_same_pair()
    {
        var first = Create(Checking, Investment).Value;
        var second = Create(Checking, Investment).Value;

        first.Out.TransferPairId.ShouldNotBe(second.Out.TransferPairId);
    }
}
