using CsCheck;
using Prisma.Domain.Investments;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Domain.Tests.Investments;

// docs/renda-fixa.md, R.9: a renda fixa entra na carteira como um grupo só ("Renda fixa", todos os índices), pesado pelo
// aportado (o custo da renda fixa), com uma linha por objetivo e o rendimento ao lado. Os pesos dos ativos passam a ser
// sobre o total com a renda fixa.
public sealed class PortfolioFixedIncomeTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Guid Itub3 = Guid.CreateVersion7();
    private static readonly Guid Reserve = Guid.CreateVersion7();
    private static readonly Guid Car = Guid.CreateVersion7();

    // 100 ações a R$ 30,00: custo R$ 3.000,00.
    private static readonly TradeNote Stocks = TradeNote.Create(
        Guid.CreateVersion7(), "Íon", new DateOnly(2026, 3, 2), [new Trade(Itub3, AssetKind.Stock, TradeSide.Buy, 100, 3_000)], 0, 0,
        TradeNote.StockOnly).Value;

    private static Result<PortfolioSnapshot> Snapshot(int year, IReadOnlyList<FixedIncomeHolding>? fixedIncome, TradeNote[]? notes = null)
    {
        var positions = PositionCalculator.Build(notes ?? [Stocks], [], _ => "ITUB3").Value;
        return PortfolioSnapshot.Build(year, Today, positions, [], _ => AssetKind.Stock, _ => "ITUB3", fixedIncome);
    }

    private static FixedIncomeHolding Holding(Guid? goal, int deposits, long principal, long yield) => new(goal, deposits, principal, yield);

    [Fact]
    public void Fixed_income_is_one_group_weighted_by_the_principal()
    {
        // Ações R$ 3.000,00 e renda fixa R$ 7.000,00: 30% e 70%.
        var snapshot = Snapshot(2026, [Holding(Reserve, 2, 500_000, 9_000), Holding(Car, 1, 200_000, 1_000)]).Value;

        snapshot.InvestedCents.ShouldBe(1_000_000);
        snapshot.Groups.Select(g => (g.Group, g.InvestedCents, g.WeightBasisPoints)).ToList().ShouldBe(
            [(PortfolioGroup.FixedIncome, 700_000L, 7_000), (PortfolioGroup.Stocks, 300_000L, 3_000)]);
        snapshot.Groups[0].AssetCount.ShouldBe(2); // dois objetivos
    }

    [Fact]
    public void Each_line_is_a_goal_with_its_weight_and_yield_ordered_by_the_principal()
    {
        var snapshot = Snapshot(2026, [Holding(Car, 1, 200_000, 1_000), Holding(Reserve, 2, 500_000, 9_000)]).Value;

        snapshot.FixedIncome.Select(l => (l.GoalId, l.DepositCount, l.PrincipalCents, l.GrossYieldCents, l.WeightBasisPoints)).ToList()
            .ShouldBe([(Reserve, 2, 500_000L, 9_000L, 5_000), (Car, 1, 200_000L, 1_000L, 2_000)]);
    }

    [Fact]
    public void The_loose_deposits_are_a_line_without_a_goal()
    {
        var snapshot = Snapshot(2026, [Holding(null, 1, 100_000, 0)]).Value;

        snapshot.FixedIncome.ShouldHaveSingleItem().GoalId.ShouldBeNull();
    }

    [Fact]
    public void The_asset_weights_are_over_the_total_with_fixed_income()
    {
        var snapshot = Snapshot(2026, [Holding(Reserve, 1, 700_000, 0)]).Value;

        snapshot.Assets.Single().WeightBasisPoints.ShouldBe(3_000);
    }

    [Fact]
    public void Without_fixed_income_nothing_changes()
    {
        var plain = Snapshot(2026, null).Value;
        var empty = Snapshot(2026, []).Value;

        plain.FixedIncome.ShouldBeEmpty();
        plain.Groups.Select(g => g.Group).ShouldBe([PortfolioGroup.Stocks]);
        plain.Assets.Single().WeightBasisPoints.ShouldBe(10_000);
        empty.InvestedCents.ShouldBe(plain.InvestedCents);
        empty.Groups.Count.ShouldBe(plain.Groups.Count);
    }

    [Fact]
    public void A_goal_with_no_principal_is_left_out()
    {
        var snapshot = Snapshot(2026, [Holding(Reserve, 0, 0, 0), Holding(Car, 1, 200_000, 0)]).Value;

        snapshot.FixedIncome.ShouldHaveSingleItem().GoalId.ShouldBe(Car);
        snapshot.Groups.Single(g => g.Group == PortfolioGroup.FixedIncome).AssetCount.ShouldBe(1);
    }

    [Fact]
    public void Only_fixed_income_is_a_portfolio_too()
    {
        var snapshot = Snapshot(2026, [Holding(Reserve, 1, 500_000, 0)], notes: []).Value;

        snapshot.InvestedCents.ShouldBe(500_000);
        snapshot.Groups.ShouldHaveSingleItem().WeightBasisPoints.ShouldBe(10_000);
        snapshot.Assets.ShouldBeEmpty();
    }

    // Empate de custo entre grupos: vale a ordem dos grupos (as ações vêm antes da renda fixa).
    [Fact]
    public void A_tie_between_groups_follows_the_group_order()
    {
        var snapshot = Snapshot(2026, [Holding(Reserve, 1, 300_000, 0)]).Value;

        snapshot.Groups.Select(g => g.Group).ShouldBe([PortfolioGroup.Stocks, PortfolioGroup.FixedIncome]);
    }

    [Fact]
    public void Fixed_income_has_no_payouts_or_realized_result_of_its_own()
    {
        var group = Snapshot(2026, [Holding(Reserve, 1, 500_000, 9_000)]).Value.Groups.Single(g => g.Group == PortfolioGroup.FixedIncome);

        (group.PayoutsCents, group.RealizedCents).ShouldBe((0L, 0L));
    }

    [Fact]
    public void A_negative_value_is_refused()
    {
        var result = Snapshot(2026, [Holding(Reserve, 1, -1, 0)]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    // Para quaisquer valores, os pesos de ativos e linhas somam 100% e os dos grupos também.
    [Fact]
    public void Weights_always_close_at_one_hundred_percent()
    {
        Gen.Long[1, 1_000_000_000].List[1, 6].Sample(
            principals =>
            {
                var lines = principals.Select(p => Holding(Guid.CreateVersion7(), 1, p, 0)).ToList();
                var snapshot = Snapshot(2026, lines).Value;

                (snapshot.Assets.Sum(a => a.WeightBasisPoints) + snapshot.FixedIncome.Sum(l => l.WeightBasisPoints)).ShouldBe(10_000);
                snapshot.Groups.Sum(g => g.WeightBasisPoints).ShouldBe(10_000);
                snapshot.InvestedCents.ShouldBe(300_000 + principals.Sum());
            },
            iter: 2_000);
    }
}
