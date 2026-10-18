using System.Text.Json;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// Regressão contra referências de fora (docs/renda-fixa.md, seção 9): os casos de fixed-income-reference.json, cada um com a
// origem do número (cálculo independente ou extrato do banco). Quem mexer no motor e mudar algum resultado vê aqui qual caso quebrou.
public sealed class FixedIncomeReferenceTests
{
    private sealed record Independent(
        string Name, string Source, DateOnly DepositDate, long PrincipalCents, long PercentE4, DateOnly AsOf, int RatesUsed, long FactorE8, long GrossCents);

    private sealed record Lot(DateOnly DepositDate, long PrincipalCents, long PercentE4);

    private sealed record Bank(string Name, string Source, DateOnly AsOf, long BankGrossYieldCents, long ToleranceCents, List<Lot> Lots);

    private sealed record Reference(List<Independent> Independent, List<Bank> Bank);

    private static readonly Reference Cases = JsonSerializer.Deserialize<Reference>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FixedIncome", "fixed-income-reference.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static IEnumerable<object[]> IndependentNames => Cases.Independent.Select(c => new object[] { c.Name });

    public static IEnumerable<object[]> BankNames => Cases.Bank.Select(c => new object[] { c.Name });

    [Fact]
    public void The_file_has_cases_and_every_case_names_its_source()
    {
        Cases.Independent.Count.ShouldBeGreaterThanOrEqualTo(9);
        Cases.Bank.Count.ShouldBe(2);
        Cases.Independent.ShouldAllBe(c => c.Source.Length > 0);
        Cases.Bank.ShouldAllBe(c => c.Source.Length > 0 && c.ToleranceCents >= 0);
    }

    [Theory]
    [MemberData(nameof(IndependentNames))]
    public void The_engine_matches_the_independent_calculation_exactly(string name)
    {
        var c = Cases.Independent.Single(x => x.Name == name);

        var balance = FixedIncomeEngine.Balance(
            new Money(c.PrincipalCents), c.DepositDate, new IndexPercent(c.PercentE4), Cdi2026.History, c.AsOf).Value;

        balance.FactorE8.ShouldBe(c.FactorE8);
        balance.GrossBalance.ShouldBe(new Money(c.GrossCents));
        balance.RatesUsed.ShouldBe(c.RatesUsed);
    }

    // Contra o banco: dentro da tolerância declarada no próprio caso, nunca "maior que o investido".
    [Theory]
    [MemberData(nameof(BankNames))]
    public void The_group_yield_is_within_the_declared_tolerance_of_the_bank_statement(string name)
    {
        var c = Cases.Bank.Single(x => x.Name == name);

        var totals = FixedIncomeTotals.Of(c.Lots.Select(lot => FixedIncomeEngine.Balance(
            new Money(lot.PrincipalCents), lot.DepositDate, new IndexPercent(lot.PercentE4), Cdi2026.History, c.AsOf).Value));

        Math.Abs(totals.GrossYield.Cents - c.BankGrossYieldCents).ShouldBeLessThanOrEqualTo(c.ToleranceCents);
    }
}
