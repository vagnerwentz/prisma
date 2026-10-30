using Prisma.Domain.Accounts;
using Shouldly;

namespace Prisma.Domain.Tests.Accounts;

// docs/patrimonio.md, I.9: o "Investido" vem dos ativos; o caixa das corretoras só entra se a conta o acompanha.
public sealed class NetWorthSplitTests
{
    private static NetWorthAccount Checking(long balance, bool active = true) => new(AccountType.Checking, active, false, balance, 0);

    private static NetWorthAccount Cash(long balance) => new(AccountType.Cash, true, false, balance, 0);

    private static NetWorthAccount Broker(long balance, bool tracksCash, long fundedPrincipal = 0, bool active = true) =>
        new(AccountType.Investment, active, tracksCash, balance, fundedPrincipal);

    [Fact]
    public void Without_accounts_or_assets_everything_is_zero() =>
        NetWorthSplit.Of([], 0, 0).ShouldBe(new NetWorthSplit(0, 0, 0, 0));

    // O achado do dono: 16 aportes de renda fixa e só um tinha trilha de caixa. Agora todos contam, e a conta não importa.
    [Fact]
    public void Invested_is_what_the_assets_say_whatever_the_cash_trail()
    {
        var split = NetWorthSplit.Of([Checking(425_030)], investedCents: 14_175_384 + 1_000_000 - 1_039_544 + 574_970, yieldCents: 40_700);

        // Principal que sobra (14.175.384 + 1.000.000 − 1.039.544 + 574.970) mais o rendimento de 40.700.
        split.InvestedCents.ShouldBe(14_751_510);
        split.AvailableCents.ShouldBe(425_030);
        split.BrokerCashCents.ShouldBe(0);
        split.TotalCents.ShouldBe(15_176_540);
    }

    [Fact]
    public void An_investment_account_that_does_not_track_cash_is_left_out_of_every_sum()
    {
        var split = NetWorthSplit.Of([Checking(425_030), Broker(446_941, tracksCash: false)], 574_970, 0);

        split.ShouldBe(new NetWorthSplit(425_030, 574_970, 0, 1_000_000));
    }

    [Fact]
    public void Free_cash_is_the_balance_minus_the_principal_the_account_financed()
    {
        // R$ 10.000,00 viraram um CDB (que já está em Invested) e R$ 2.500,00 seguem parados na corretora.
        var split = NetWorthSplit.Of([Broker(1_250_000, tracksCash: true, fundedPrincipal: 1_000_000)], 1_000_000, 0);

        split.BrokerCashCents.ShouldBe(250_000);
        split.TotalCents.ShouldBe(1_250_000);
    }

    [Fact]
    public void Free_cash_can_be_negative_when_the_history_is_incomplete_and_is_not_hidden()
    {
        var split = NetWorthSplit.Of([Broker(446_941, tracksCash: true, fundedPrincipal: 1_000_000)], 1_000_000, 0);

        split.BrokerCashCents.ShouldBe(-553_059);
    }

    // O caso do dono com o caixa acompanhado: R$ 5.749,70 transferidos e gastos pela nota vinculada. O saldo da Íon zera, a compra
    // está em Invested pelo custo, e o total não muda.
    [Fact]
    public void A_linked_note_does_not_count_twice()
    {
        var before = NetWorthSplit.Of([Checking(425_030), Broker(574_970, tracksCash: true)], 0, 0);
        var after = NetWorthSplit.Of([Checking(425_030), Broker(0, tracksCash: true)], 574_970, 0);

        before.BrokerCashCents.ShouldBe(574_970);
        after.ShouldBe(new NetWorthSplit(425_030, 574_970, 0, 1_000_000));
        after.TotalCents.ShouldBe(before.TotalCents);
    }

    [Fact]
    public void Inactive_accounts_and_cards_are_left_out()
    {
        var card = new NetWorthAccount(AccountType.CreditCard, true, false, 99_999, 0);

        var split = NetWorthSplit.Of([Checking(100, active: false), Broker(777, true, active: false), card, Cash(50)], 0, 0);

        split.ShouldBe(new NetWorthSplit(50, 0, 0, 50));
    }

    [Fact]
    public void Cash_accounts_and_checking_accounts_are_both_available() =>
        NetWorthSplit.Of([Checking(1_000), Cash(250)], 0, 0).AvailableCents.ShouldBe(1_250);

    [Fact]
    public void Yield_is_part_of_what_is_invested() =>
        NetWorthSplit.Of([], 1_000_000, 40_700).InvestedCents.ShouldBe(1_040_700);

    // Ligar ou desligar o caixa de uma conta vazia, sem aporte financiado, não mexe em nada.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tracking_an_empty_account_changes_nothing(bool tracks) =>
        NetWorthSplit.Of([Checking(1_000), Broker(0, tracks)], 500, 10).ShouldBe(NetWorthSplit.Of([Checking(1_000)], 500, 10));

    [Fact]
    public void The_total_is_always_the_sum_of_the_three_parts()
    {
        var split = NetWorthSplit.Of([Checking(1), Cash(2), Broker(30, true, 7), Broker(400, false)], 5_000, 60);

        split.TotalCents.ShouldBe(split.AvailableCents + split.InvestedCents + split.BrokerCashCents);
    }

    [Theory]
    [InlineData(1_250_000, 1_000_000, 250_000)]
    [InlineData(446_941, 1_000_000, -553_059)]
    [InlineData(0, 0, 0)]
    public void Free_cash_of_one_account_is_its_balance_minus_what_it_financed(long balance, long funded, long expected) =>
        Broker(balance, tracksCash: true, fundedPrincipal: funded).FreeCashCents.ShouldBe(expected);
}
