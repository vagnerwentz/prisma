using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 2.15 (docs/fase-2.md, 2.8), com o exemplo da regra. Hoje é 25/09/2026.
public sealed class DescriptionHistoryTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly Guid Nubank = Guid.NewGuid();
    private static readonly Guid Itau = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Leisure = Guid.NewGuid();
    private static readonly Guid Health = Guid.NewGuid();
    private static readonly Guid Salary = Guid.NewGuid();
    private static readonly Guid Shopping = Guid.NewGuid();

    private static int _sequence;

    private static DescriptionUse Use(
        string description, string date, Guid account, PaymentMethod method, Guid? category, long cents,
        TransactionType type = TransactionType.Expense, int? installment = null, long? purchaseTotal = null) =>
        new(type, description, category, account, method, cents, DateOnly.Parse(date), installment, purchaseTotal,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_sequence++));

    // O exemplo da seção 2.8. As 10 parcelas do Notebook vêm todas, como vêm do banco.
    private static IEnumerable<DescriptionUse> SpecExample()
    {
        yield return Use("iFood", "2026-09-20", Nubank, PaymentMethod.Credit, Food, 4290);
        yield return Use("ifood ", "2026-09-10", Itau, PaymentMethod.Pix, Leisure, 3500);
        yield return Use("Farmácia São João", "2026-09-05", Itau, PaymentMethod.Pix, Health, 6480);
        yield return Use("farmacia  sao joao", "2026-08-01", Itau, PaymentMethod.Pix, Health, 3000);
        yield return Use("Salário", "2026-09-05", Itau, PaymentMethod.Pix, Salary, 820000, TransactionType.Income);
        for (var n = 1; n <= 10; n++)
            yield return Use("Notebook", "2026-03-15", Nubank, PaymentMethod.Credit, Shopping, 60000, installment: n, purchaseTotal: 600000);
        yield return Use("Estorno: iFood", "2026-09-12", Nubank, PaymentMethod.Credit, Food, 1000, TransactionType.Refund);
        yield return Use("Saque", "2026-09-05", Itau, PaymentMethod.Pix, null, 50000, TransactionType.Transfer);
        yield return Use("Mercado", "2025-09-24", Itau, PaymentMethod.Debit, Food, 20000);
        yield return Use("   ", "2026-09-18", Itau, PaymentMethod.Pix, Food, 1500);
    }

    [Fact]
    public void Spec_example_grouped_most_recent_wins_most_used_first()
    {
        var history = DescriptionHistory.Of(SpecExample(), Today);

        history.Select(s => (s.Description, s.Type, s.CategoryId, s.AccountId, s.Method, s.LastAmountCents, s.Count, s.LastUsedOn))
            .ShouldBe(
            [
                ("iFood", TransactionType.Expense, (Guid?)Food, Nubank, PaymentMethod.Credit, 4290L, 2, new DateOnly(2026, 9, 20)),
                ("Farmácia São João", TransactionType.Expense, Health, Itau, PaymentMethod.Pix, 6480L, 2, new DateOnly(2026, 9, 5)),
                ("Salário", TransactionType.Income, Salary, Itau, PaymentMethod.Pix, 820000L, 1, new DateOnly(2026, 9, 5)),
                ("Notebook", TransactionType.Expense, Shopping, Nubank, PaymentMethod.Credit, 600000L, 1, new DateOnly(2026, 3, 15)),
            ]);
    }

    [Fact]
    public void The_key_ignores_case_accents_and_extra_spaces()
    {
        DescriptionHistory.Key("  Farmácia   São João ").ShouldBe("farmacia sao joao");
        DescriptionHistory.Key("PÃO DE AÇÚCAR").ShouldBe("pao de acucar");
    }

    [Fact]
    public void The_same_description_as_expense_and_as_income_are_separate()
    {
        var history = DescriptionHistory.Of(
        [
            Use("Freelance", "2026-09-01", Itau, PaymentMethod.Pix, Salary, 100000, TransactionType.Income),
            Use("Freelance", "2026-09-02", Itau, PaymentMethod.Pix, Shopping, 5000),
        ], Today);

        history.Select(s => s.Type).ShouldBe([TransactionType.Expense, TransactionType.Income], ignoreOrder: true);
    }

    [Fact]
    public void Same_day_ties_go_to_the_one_created_last()
    {
        var history = DescriptionHistory.Of(
        [
            Use("Uber", "2026-09-20", Itau, PaymentMethod.Pix, Food, 1000),
            Use("uber", "2026-09-20", Nubank, PaymentMethod.Credit, Leisure, 2000),
        ], Today);

        var uber = history.ShouldHaveSingleItem();
        uber.Description.ShouldBe("uber");
        uber.AccountId.ShouldBe(Nubank);
    }

    [Fact]
    public void Twelve_months_back_is_the_first_day_that_counts()
    {
        var history = DescriptionHistory.Of(
        [
            Use("Dentro", "2025-09-25", Itau, PaymentMethod.Pix, Health, 1000),
            Use("Fora", "2025-09-24", Itau, PaymentMethod.Pix, Health, 1000),
        ], Today);

        history.Select(s => s.Description).ShouldBe(["Dentro"]);
        DescriptionHistory.Since(Today).ShouldBe(new DateOnly(2025, 9, 25));
    }

    [Fact]
    public void Ties_in_use_go_to_the_most_recent_then_to_the_description()
    {
        var history = DescriptionHistory.Of(
        [
            Use("Padaria", "2026-09-10", Itau, PaymentMethod.Pix, Food, 1000),
            Use("Banca", "2026-09-10", Itau, PaymentMethod.Pix, Food, 1000),
            Use("Açougue", "2026-09-12", Itau, PaymentMethod.Pix, Food, 1000),
        ], Today);

        history.Select(s => s.Description).ShouldBe(["Açougue", "Banca", "Padaria"]);
    }

    [Fact]
    public void At_most_three_hundred_groups()
    {
        var uses = Enumerable.Range(0, 350).Select(i => Use($"Loja {i}", "2026-09-01", Itau, PaymentMethod.Pix, Food, 100));

        DescriptionHistory.Of(uses, Today).Count.ShouldBe(DescriptionHistory.MaxItems);
    }
}
