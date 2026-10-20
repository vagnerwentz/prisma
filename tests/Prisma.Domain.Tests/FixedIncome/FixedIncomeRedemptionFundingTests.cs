using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O resgate que cai numa conta (docs/renda-fixa-resgate.md, 5): o principal volta como transferência, da conta de investimento para a
// conta de destino, e o rendimento líquido entra como receita. O imposto não é despesa: já vem abatido. Os valores são os do resgate de
// R$ 3.000,00 de FixedIncomeRedemptionTests (principal 293.987, rendimento 6.013, IR 1.353, líquido 298.647).
public sealed class FixedIncomeRedemptionFundingTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Day = new(2026, 9, 15);

    private static Account Make(string name, AccountType type) =>
        type == AccountType.CreditCard
            ? Account.Create(User, name, type, 0, 26, 5, null).Value
            : Account.Create(User, name, type, 0, null, null, null).Value;

    private static readonly Account Checking = Make("Itaú", AccountType.Checking);
    private static readonly Account Cash = Make("Carteira", AccountType.Cash);
    private static readonly Account Investment = Make("Itaú Investimentos", AccountType.Investment);
    private static readonly Account Card = Make("Visa", AccountType.CreditCard);
    private static readonly Category Income = Category.Create(User, "Rendimentos", TransactionType.Income, null, "sprout", "#A78BFA").Value;
    private static readonly Category Expense = Category.Create(User, "Mercado", TransactionType.Expense, null, "sprout", "#A78BFA").Value;

    private static FixedIncomeRedemption Redemption(long gross = 300_000, long? net = null, long cents = 1_000_000)
    {
        var deposit = FixedIncomeDeposit.Create(User, null, new DateOnly(2026, 7, 21), cents, FixedIncomeProductKind.Cdb,
            FixedIncomeIndexKind.Cdi, 1_000_000, null, DepositLiquidity.Unknown, null, Guid.NewGuid(), Today).Value;
        return FixedIncomeRedemption.Create(deposit, [], Cdi2026.History, Day, gross, net, Guid.NewGuid(), Today).Value;
    }

    private static Result<RedemptionLegs> Fund(Account from, Account to, FixedIncomeRedemption? redemption = null, Category? category = null) =>
        FixedIncomeRedemptionFunding.Create(User, from, to, redemption ?? Redemption(), category ?? Income);

    [Fact]
    public void The_principal_comes_back_as_a_transfer_from_the_investment_account()
    {
        var redemption = Redemption();

        var legs = Fund(Investment, Checking, redemption).Value.Transfer!;

        legs.Out.AccountId.ShouldBe(Investment.Id);
        legs.In.AccountId.ShouldBe(Checking.Id);
        (legs.Out.TransferDirection, legs.In.TransferDirection).ShouldBe((TransferDirection.Out, TransferDirection.In));
        (legs.Out.AmountCents, legs.In.AmountCents).ShouldBe((293_987L, 293_987L));
        (legs.Out.PurchaseDate, legs.In.SettlementDate).ShouldBe((Day, Day));
        legs.Out.Type.ShouldBe(TransactionType.Transfer);
        legs.Out.Description.ShouldBe(FixedIncomeRedemptionFunding.TransferDescription);
        redemption.TransferPairId.ShouldBe(legs.Out.TransferPairId);
    }

    [Fact]
    public void The_net_yield_is_income_in_the_destination_account_and_the_tax_is_not_an_expense()
    {
        var redemption = Redemption();

        var income = Fund(Investment, Checking, redemption).Value.Income!;

        // Rendimento 6.013 menos IR 1.353: 4.660 de receita. O total que cai na conta é o líquido, 298.647 = 293.987 + 4.660.
        income.Type.ShouldBe(TransactionType.Income);
        income.AmountCents.ShouldBe(4_660);
        income.AccountId.ShouldBe(Checking.Id);
        income.PurchaseDate.ShouldBe(Day);
        income.SettlementDate.ShouldBe(Day);
        income.CategoryId.ShouldBe(Income.Id);
        income.Description.ShouldBe(FixedIncomeRedemptionFunding.IncomeDescription);
        (293_987 + income.AmountCents).ShouldBe(redemption.NetCents);
        redemption.IncomeTransactionId.ShouldBe(income.Id);
    }

    [Fact]
    public void The_wallet_can_receive_the_money_too() => Fund(Investment, Cash).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Without_the_category_the_income_has_none()
    {
        var legs = FixedIncomeRedemptionFunding.Create(User, Investment, Checking, Redemption(), null).Value;

        legs.Income!.CategoryId.ShouldBeNull();
    }

    [Fact]
    public void An_informed_net_sets_the_income()
    {
        // O banco pagou 298.700: o rendimento líquido é 298.700 − 293.987 = 4.713.
        var legs = Fund(Investment, Checking, Redemption(net: 298_700)).Value;

        legs.Income!.AmountCents.ShouldBe(4_713);
        legs.Transfer!.Out.AmountCents.ShouldBe(293_987);
    }

    [Fact]
    public void With_no_net_yield_there_is_only_the_transfer()
    {
        // Imposto = todo o rendimento (o limite do líquido informado): líquido = principal, nada de receita.
        var redemption = Redemption(net: 293_987);

        var legs = Fund(Investment, Checking, redemption).Value;

        legs.Income.ShouldBeNull();
        legs.Transfer!.Out.AmountCents.ShouldBe(293_987);
        redemption.IncomeTransactionId.ShouldBeNull();
    }

    [Fact]
    public void With_no_principal_there_is_only_the_income()
    {
        // Um centavo a 100 % ao ano por mais de três anos: o resgate parcial pode ser só de rendimento (principal zero).
        var deposit = FixedIncomeDeposit.Create(User, null, new DateOnly(2023, 1, 2), 1, FixedIncomeProductKind.Lca,
            FixedIncomeIndexKind.Prefixed, 1_000_000, null, DepositLiquidity.Unknown, null, Guid.NewGuid(), Today).Value;
        var empty = RateHistory.Create([]).Value;
        var redemption = FixedIncomeRedemption.Create(deposit, [], empty, Today, 12, null, Guid.NewGuid(), Today).Value;
        redemption.PrincipalCents.ShouldBe(0);

        var legs = Fund(Investment, Checking, redemption).Value;

        legs.Transfer.ShouldBeNull();
        legs.Income!.AmountCents.ShouldBe(12);
        redemption.TransferPairId.ShouldBeNull();
    }

    [Fact]
    public void The_money_leaves_an_investment_account_and_lands_in_a_checking_account_or_the_wallet()
    {
        Fund(Checking, Cash).Error!.Code.ShouldBe("RedemptionNeedsInvestmentAccount");
        Fund(Investment, Investment).Error!.Code.ShouldBe("RedemptionNeedsPaymentAccount");
        Fund(Investment, Card).Error!.Code.ShouldBe("RedemptionNeedsPaymentAccount");
        Fund(Investment, Card).Error!.Message.ShouldBe("O dinheiro do resgate cai numa conta corrente ou na carteira.");
    }

    [Fact]
    public void The_income_category_must_be_an_income_one() =>
        Fund(Investment, Checking, category: Expense).IsSuccess.ShouldBeFalse();

    [Fact]
    public void Unlinking_forgets_both_ends()
    {
        var redemption = Redemption();
        Fund(Investment, Checking, redemption);

        redemption.Unlink();

        (redemption.TransferPairId, redemption.IncomeTransactionId).ShouldBe((null, null));
    }
}
