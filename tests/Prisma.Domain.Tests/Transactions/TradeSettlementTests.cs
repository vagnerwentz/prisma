using Prisma.Domain.Accounts;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/investimentos.md, "I.8": a liquidação da nota no caixa da conta. O valor sai de TradeNote.NetCashCents.
public sealed class TradeSettlementTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 22);
    private static readonly Guid Petr4 = Guid.NewGuid();
    private static readonly Guid Vale3 = Guid.NewGuid();

    private static Account Make(AccountType type) =>
        type == AccountType.CreditCard
            ? Account.Create(UserId, "Visa", type, 0, 26, 5, null).Value
            : Account.Create(UserId, "Íon", type, 0, null, null, null).Value;

    private static Trade Buy(Guid asset, long quantity, long priceCents) => new(asset, AssetKind.Stock, TradeSide.Buy, quantity, priceCents);

    private static Trade Sell(Guid asset, long quantity, long priceCents) => new(asset, AssetKind.Stock, TradeSide.Sell, quantity, priceCents);

    private static TradeNote Note(Trade[] trades, long fees = 0) =>
        TradeNote.Create(UserId, "Íon", Day, trades, fees, 0, TradeNote.StockOnly).Value;

    [Fact]
    public void A_buy_becomes_an_outgoing_settlement_for_the_net_cash()
    {
        var account = Make(AccountType.Investment);
        var note = Note([Buy(Petr4, 100, 5700)], fees: 4970);

        var settlement = TradeSettlement.Settle(note, account, existing: null)!;

        (settlement.Type, settlement.TransferDirection, settlement.AmountCents).ShouldBe((TransactionType.TradeSettlement, TransferDirection.Out, 574970L));
        (settlement.AccountId, settlement.TradeNoteId, settlement.UserId).ShouldBe((account.Id, note.Id, UserId));
        (settlement.PurchaseDate, settlement.SettlementDate, settlement.TransferPairId, settlement.StatementId).ShouldBe((Day, Day, null, null));
        settlement.Description.ShouldBe("Nota de corretagem · Íon");
    }

    [Fact]
    public void A_sell_becomes_an_incoming_settlement()
    {
        var note = Note([Sell(Petr4, 100, 6000)], fees: 5000);

        var settlement = TradeSettlement.Settle(note, Make(AccountType.Investment), null)!;

        (settlement.TransferDirection, settlement.AmountCents).ShouldBe((TransferDirection.In, 595000L));
    }

    [Fact]
    public void Without_an_account_or_with_net_zero_there_is_no_settlement()
    {
        TradeSettlement.Settle(Note([Buy(Petr4, 10, 10000)]), account: null, null).ShouldBeNull();
        TradeSettlement.Settle(Note([Buy(Petr4, 10, 10000), Sell(Vale3, 10, 10000)]), Make(AccountType.Investment), null).ShouldBeNull();
    }

    [Fact]
    public void Settling_again_updates_the_same_transaction()
    {
        var first = Make(AccountType.Investment);
        var second = Make(AccountType.Checking);
        var settlement = TradeSettlement.Settle(Note([Buy(Petr4, 100, 5700)]), first, null)!;

        var again = TradeSettlement.Settle(Note([Sell(Petr4, 100, 5800)]), second, settlement)!;

        again.ShouldBeSameAs(settlement);
        (again.AccountId, again.TransferDirection, again.AmountCents).ShouldBe((second.Id, TransferDirection.In, 580000L));
    }

    [Fact]
    public void A_credit_card_cannot_settle_a_note() =>
        TradeSettlement.CheckAccount(Make(AccountType.CreditCard))!.Message.ShouldBe("O cartão de crédito não liquida nota de corretagem.");

    [Theory]
    [InlineData(AccountType.Checking)]
    [InlineData(AccountType.Cash)]
    [InlineData(AccountType.Investment)]
    public void Checking_cash_and_investment_accounts_can_settle(AccountType type) =>
        TradeSettlement.CheckAccount(Make(type)).ShouldBeNull();

    [Fact]
    public void The_settlement_is_not_edited_by_hand()
    {
        var settlement = TradeSettlement.Settle(Note([Buy(Petr4, 100, 5700)]), Make(AccountType.Investment), null)!;

        settlement.CheckNotTradeSettlement()!.Type.ShouldBe(ErrorType.Conflict);
        settlement.CheckCanChangeIndividually()!.Message.ShouldBe("Altere ou exclua a nota de corretagem.");
        settlement.UpdateSimple(Make(AccountType.Checking), TransactionType.Expense, 100, Day, null, PaymentMethod.Pix, null)
            .Error!.Message.ShouldBe("Altere ou exclua a nota de corretagem.");
    }
}
