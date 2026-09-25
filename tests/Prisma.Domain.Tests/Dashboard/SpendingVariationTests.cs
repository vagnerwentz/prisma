using CsCheck;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Dashboard;

// Etapa 2.10 (docs/fase-2.md, 2.7), com o exemplo da regra: setembro contra agosto de 2026.
public sealed class SpendingVariationTests
{
    private static readonly Guid Home = Guid.NewGuid();
    private static readonly Guid Leisure = Guid.NewGuid();
    private static readonly Guid Health = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Transport = Guid.NewGuid();

    private static readonly Guid TvPurchase = Guid.NewGuid();
    private static readonly Guid TicketPurchase = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [Home] = "Casa", [Leisure] = "Lazer", [Health] = "Saúde", [Food] = "Alimentação", [Transport] = "Transporte",
    };

    private static SpendingEntry Expense(Guid? category, string description, long cents) =>
        new(Guid.NewGuid(), TransactionType.Expense, category, category is { } id ? Names[id] : null, description, cents, null, null, null);

    private static SpendingEntry Refund(Guid? category, string description, long cents) =>
        new(Guid.NewGuid(), TransactionType.Refund, category, category is { } id ? Names[id] : null, description, cents, null, null, null);

    private static SpendingEntry Installment(Guid category, Guid purchase, string description, long cents, int number, int count) =>
        new(Guid.NewGuid(), TransactionType.Expense, category, Names[category], description, cents, purchase, number, count);

    private static readonly SpendingEntry[] August =
    [
        Installment(Home, TvPurchase, "TV", 40000, 2, 10),
        Expense(Leisure, "Cinema", 10000),
        Installment(Leisure, TicketPurchase, "Passagem", 60000, 1, 6),
        Expense(Food, "Mercado", 90000),
        Expense(Food, "iFood", 30000),
        Expense(Transport, "Uber", 30000),
    ];

    private static readonly SpendingEntry[] September =
    [
        Installment(Home, TvPurchase, "TV", 40000, 3, 10),
        Installment(Leisure, TicketPurchase, "Passagem", 60000, 2, 6),
        Expense(Leisure, "Show", 18000),
        Expense(Leisure, "Cinema", 7000),
        Expense(Health, "Farmácia", 15000),
        Expense(Food, "Mercado", 80000),
        Expense(Food, "iFood", 25000),
        Expense(Transport, "Uber", 30000),
    ];

    [Fact]
    public void Spec_example_reasons_in_order_with_details_adding_up_to_the_change()
    {
        var variation = SpendingVariation.Of(September, August);

        variation.ExpenseCents.ShouldBe(275000);
        variation.PreviousExpenseCents.ShouldBe(260000);
        variation.ChangeCents.ShouldBe(15000);

        variation.Reasons.Select(r => (r.Kind, r.CategoryId, r.ChangeCents)).ShouldBe(
        [
            (VariationReasonKind.Inherited, null, 60000L),
            (VariationReasonKind.Category, Leisure, -45000L),
            (VariationReasonKind.Category, Health, 15000L),
            (VariationReasonKind.Category, Food, -15000L),
        ]);
        variation.Reasons.Sum(r => r.ChangeCents).ShouldBe(variation.ChangeCents);

        var inherited = variation.Reasons[0];
        inherited.Started.Select(p => (p.PurchaseId, p.Description, p.AmountCents)).ShouldBe([(TicketPurchase, "Passagem", 60000L)]);
        inherited.Ended.ShouldBeEmpty();

        // Só a categoria que subiu mostra o maior lançamento decidido no mês.
        variation.Reasons[2].Largest!.Value.Description.ShouldBe("Farmácia");
        variation.Reasons[2].Largest!.Value.AmountCents.ShouldBe(15000);
        variation.Reasons[1].Largest.ShouldBeNull();
        variation.Reasons[3].Largest.ShouldBeNull();
    }

    [Fact]
    public void Installments_that_ended_last_month_are_the_detail_of_a_drop()
    {
        SpendingEntry[] previous = [Installment(Home, TvPurchase, "TV", 40000, 10, 10), Expense(Food, "Mercado", 50000)];
        SpendingEntry[] current = [Expense(Food, "Mercado", 50000)];

        var variation = SpendingVariation.Of(current, previous);

        var reason = variation.Reasons.ShouldHaveSingleItem();
        reason.Kind.ShouldBe(VariationReasonKind.Inherited);
        reason.ChangeCents.ShouldBe(-40000);
        reason.Ended.Select(p => p.Description).ShouldBe(["TV"]);
        reason.Started.ShouldBeEmpty();
    }

    [Fact]
    public void Beyond_three_categories_the_rest_is_one_last_line_and_the_sum_stays_exact()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var e = Guid.NewGuid();
        SpendingEntry Spent(Guid id, string name, long cents) => new(Guid.NewGuid(), TransactionType.Expense, id, name, name, cents, null, null, null);

        SpendingEntry[] previous = [Spent(a, "A", 10000), Spent(b, "B", 50000), Spent(e, "E", 30000)];
        SpendingEntry[] current =
        [
            Spent(a, "A", 90000),  // +800
            Spent(b, "B", 10000),  // −400
            Spent(c, "C", 30000),  // +300
            Spent(d, "D", 20000),  // +200, fica em "Outras"
            // E: −300, empata com C em módulo; a alta (C) vem antes, e E vai para "Outras"
        ];

        var variation = SpendingVariation.Of(current, previous);

        variation.Reasons.Select(r => (r.Kind, r.CategoryId, r.ChangeCents)).ShouldBe(
        [
            (VariationReasonKind.Category, a, 80000L),
            (VariationReasonKind.Category, b, -40000L),
            (VariationReasonKind.Category, c, 30000L),
            (VariationReasonKind.OtherCategories, null, -10000L),
        ]);
        variation.Reasons.Sum(r => r.ChangeCents).ShouldBe(variation.ChangeCents);
    }

    [Fact]
    public void Other_categories_that_cancel_out_do_not_appear()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        SpendingEntry Spent(int i, long cents) => new(Guid.NewGuid(), TransactionType.Expense, ids[i], $"C{i}", "x", cents, null, null, null);

        var variation = SpendingVariation.Of(
            [Spent(0, 90000), Spent(1, 80000), Spent(2, 70000), Spent(3, 10000)],
            [Spent(4, 10000)]);

        variation.Reasons.Count.ShouldBe(3);
        variation.Reasons.ShouldAllBe(r => r.Kind == VariationReasonKind.Category);
        variation.Reasons.Sum(r => r.ChangeCents).ShouldBe(variation.ChangeCents);
    }

    [Fact]
    public void A_refund_lowers_its_category_and_the_largest_is_a_decided_expense()
    {
        // Agosto: Alimentação R$ 500,00 com estorno de R$ 300,00 (líquido R$ 200,00). Setembro: R$ 300,00.
        SpendingEntry[] previous = [Expense(Food, "Mercado", 50000), Refund(Food, "Estorno: Mercado", 30000)];
        SpendingEntry[] current = [Expense(Food, "Mercado", 20000), Expense(Food, "Padaria", 10000)];

        var variation = SpendingVariation.Of(current, previous);

        variation.PreviousExpenseCents.ShouldBe(20000);
        var reason = variation.Reasons.ShouldHaveSingleItem();
        reason.ChangeCents.ShouldBe(10000);
        reason.Largest!.Value.Description.ShouldBe("Mercado");
    }

    [Fact]
    public void A_refund_this_month_is_a_drop_in_its_category()
    {
        SpendingEntry[] previous = [Expense(Food, "Mercado", 50000)];
        SpendingEntry[] current = [Expense(Food, "Mercado", 50000), Refund(Food, "Estorno", 20000)];

        var variation = SpendingVariation.Of(current, previous);

        variation.ChangeCents.ShouldBe(-20000);
        variation.Reasons.ShouldHaveSingleItem().ChangeCents.ShouldBe(-20000);
    }

    [Fact]
    public void Uncategorized_is_a_line_of_its_own()
    {
        var variation = SpendingVariation.Of([Expense(null, "Farmácia", 12000)], [Expense(Food, "Mercado", 5000)]);

        variation.Reasons.Select(r => (r.Kind, r.CategoryId, r.ChangeCents)).ShouldBe(
        [
            (VariationReasonKind.Category, null, 12000L),
            (VariationReasonKind.Category, Food, -5000L),
        ]);
        variation.Reasons[0].Largest!.Value.Description.ShouldBe("Farmácia");
    }

    [Fact]
    public void Ties_in_size_and_direction_go_by_name()
    {
        var variation = SpendingVariation.Of([Expense(Transport, "Uber", 10000), Expense(Food, "iFood", 10000)], []);

        variation.Reasons.Select(r => r.CategoryId).ShouldBe([Food, Transport]);
    }

    [Fact]
    public void The_same_spending_has_nothing_to_explain()
    {
        var variation = SpendingVariation.Of(August, August);

        variation.ChangeCents.ShouldBe(0);
        variation.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void Only_expenses_and_refunds_enter()
    {
        var transfer = new SpendingEntry(Guid.NewGuid(), TransactionType.Transfer, null, null, "Fatura", 1000, null, null, null);

        Should.Throw<ArgumentException>(() => SpendingVariation.Of([transfer], []));
    }

    private static readonly Gen<SpendingEntry> AnyEntry =
        Gen.Select(Gen.Int[0, 5], Gen.Long[1, 500_000], Gen.Int[0, 9], Gen.Int[1, 12]).Select((category, cents, kind, number) =>
        {
            Guid? id = category == 5 ? null : Names.Keys.ElementAt(category);
            var name = id is { } key ? Names[key] : null;
            return kind switch
            {
                0 => new SpendingEntry(Guid.NewGuid(), TransactionType.Refund, id, name, "e", cents, null, null, null),
                < 4 => new SpendingEntry(Guid.NewGuid(), TransactionType.Expense, id, name, "p", cents, Guid.NewGuid(), number, 12),
                _ => new SpendingEntry(Guid.NewGuid(), TransactionType.Expense, id, name, "d", cents, null, null, null),
            };
        });

    [Fact]
    public void For_any_two_months_the_reasons_add_up_to_the_change_and_none_is_zero()
    {
        Gen.Select(AnyEntry.Array[0, 30], AnyEntry.Array[0, 30]).Sample((current, previous) =>
        {
            var variation = SpendingVariation.Of(current, previous);

            variation.Reasons.Sum(r => r.ChangeCents).ShouldBe(variation.ChangeCents);
            variation.ChangeCents.ShouldBe(variation.ExpenseCents - variation.PreviousExpenseCents);
            variation.Reasons.ShouldAllBe(r => r.ChangeCents != 0);
            variation.Reasons.Count(r => r.Kind == VariationReasonKind.Category).ShouldBeLessThanOrEqualTo(3);
            variation.Reasons.SkipLast(1).ShouldAllBe(r => r.Kind != VariationReasonKind.OtherCategories);
        });
    }
}
