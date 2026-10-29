using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Accounts;

// docs/investimentos.md, "I.8", regra 6: o que a conta pôs em ativos = compras líquidas − vendas líquidas das notas
// vinculadas a ela (as liquidações já ocorridas), nunca abaixo de zero.
public sealed class AccountAssetsTests
{
    private static BalanceEntry Buy(long cents, bool settled = true) =>
        new(TransactionType.TradeSettlement, TransferDirection.Out, cents, settled);

    private static BalanceEntry Sell(long cents, bool settled = true) =>
        new(TransactionType.TradeSettlement, TransferDirection.In, cents, settled);

    [Fact]
    public void Without_settlements_nothing_is_in_assets() =>
        AccountAssets.Of([]).ShouldBe(0);

    [Fact]
    public void The_owners_buy_is_in_assets() =>
        AccountAssets.Of([Buy(574970)]).ShouldBe(574970);

    [Fact]
    public void Sells_come_out_of_what_is_in_assets() =>
        AccountAssets.Of([Buy(574970), Sell(200000), Buy(10000)]).ShouldBe(384970);

    [Fact]
    public void A_sell_with_profit_never_makes_it_negative() =>
        AccountAssets.Of([Buy(100000), Sell(150000)]).ShouldBe(0);

    [Fact]
    public void Only_settlements_that_already_happened_count() =>
        AccountAssets.Of([Buy(100000), Buy(50000, settled: false), Sell(30000, settled: false)]).ShouldBe(100000);

    [Fact]
    public void Other_kinds_of_entry_are_not_assets()
    {
        AccountAssets.Of(
        [
            new BalanceEntry(TransactionType.Transfer, TransferDirection.In, 574970, true),
            new BalanceEntry(TransactionType.Expense, null, 1000, true),
            new BalanceEntry(TransactionType.Income, null, 1000, true),
            Buy(2000),
        ]).ShouldBe(2000);
    }
}
