using CsCheck;
using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/fase-1.md, 2.2: editar a compra redistribui as parcelas não pagas e mantém a soma exata.
// Cartão que fecha dia 5 e vence dia 12; compra em 10/03/2026 (1ª parcela na fatura de abril).
public sealed class CardPurchaseEditTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Card =
        Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;

    private static readonly Category Electronics =
        Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;

    private static readonly Category Home =
        Category.Create(UserId, "Casa", TransactionType.Expense, null, null, null).Value;

    private static readonly Category Salary =
        Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    // Estado persistido de uma compra: parcelas ativas e faturas do cartão.
    private sealed class Stored
    {
        public required InstallmentPurchase Purchase { get; init; }
        public required List<Transaction> Installments { get; init; }
        public required List<Statement> Statements { get; init; }

        public Result<CardPurchaseEditResult> Edit(long total, int count, Category? category = null, string? description = "Notebook")
        {
            var result = CardPurchase.Edit(Purchase, Card, Installments, Statements, total, count, category ?? Electronics, description);
            if (result.IsSuccess)
            {
                Installments.RemoveAll(t => result.Value.Removed.Contains(t));
                Installments.AddRange(result.Value.Added);
                Statements.AddRange(result.Value.OpenedStatements);
            }
            return result;
        }

        public Statement StatementOf(Transaction installment) => Statements.Single(s => s.Id == installment.StatementId);
    }

    private static Stored Buy(long total, int count)
    {
        var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count,
            March10, Electronics, "Notebook", []).Value;
        return new Stored
        {
            Purchase = created.Purchase!,
            Installments = created.Installments.ToList(),
            Statements = created.OpenedStatements.ToList(),
        };
    }

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void Changing_the_total_redistributes_every_installment()
    {
        var stored = Buy(100000, 10);

        var result = stored.Edit(120003, 10);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Added.ShouldBeEmpty();
        result.Value.Removed.ShouldBeEmpty();
        stored.Installments.Select(t => t.AmountCents).ShouldBe([12001, 12001, 12001, 12000, 12000, 12000, 12000, 12000, 12000, 12000]);
        stored.Purchase.TotalAmountCents.ShouldBe(120003);
    }

    [Fact]
    public void Fewer_installments_remove_the_last_ones()
    {
        var stored = Buy(100005, 10);
        var lastSeven = stored.Installments.Skip(3).ToList();

        var result = stored.Edit(100005, 3);

        result.Value.Removed.ShouldBe(lastSeven, ignoreOrder: true);
        stored.Installments.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3]);
        stored.Installments.Select(t => t.AmountCents).ShouldBe([33335, 33335, 33335]);
        stored.Purchase.InstallmentCount.ShouldBe(3);
    }

    [Fact]
    public void More_installments_enter_the_following_cycles()
    {
        var stored = Buy(30000, 3);

        var result = stored.Edit(30000, 5);

        result.Value.Added.Select(t => t.InstallmentNumber).ShouldBe([4, 5]);
        result.Value.OpenedStatements.Select(s => s.Reference).ShouldBe(["2026-07", "2026-08"]);
        stored.Installments.Select(t => t.AmountCents).ShouldBe([6000, 6000, 6000, 6000, 6000]);
        stored.Installments.Select(t => t.SettlementDate).ShouldBe(
            Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 4, 12).AddMonths(i)));
        result.Value.Added.ShouldAllBe(t =>
            t.InstallmentPurchaseId == stored.Purchase.Id && t.PurchaseDate == March10 && t.Method == PaymentMethod.Credit);
    }

    [Fact]
    public void Existing_statements_are_reused_for_new_installments()
    {
        var stored = Buy(30000, 3);
        var july = Statement.Open(UserId, Card.Id, new StatementDates("2026-07", new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 12)));
        stored.Statements.Add(july);

        var result = stored.Edit(40000, 4);

        result.Value.OpenedStatements.ShouldBeEmpty();
        result.Value.Added.ShouldHaveSingleItem().StatementId.ShouldBe(july.Id);
    }

    [Fact]
    public void Paid_installments_keep_their_amount_and_only_the_rest_is_redistributed()
    {
        var stored = Buy(100000, 10);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();
        stored.StatementOf(stored.Installments[1]).MarkAsPaid();

        stored.Edit(90000, 10).IsSuccess.ShouldBeTrue();

        // 90000 - 2 × 10000 pagas = 70000 em 8 parcelas.
        stored.Installments.Select(t => t.AmountCents).ShouldBe([10000, 10000, 8750, 8750, 8750, 8750, 8750, 8750, 8750, 8750]);
    }

    [Fact]
    public void Paid_installments_cannot_be_removed()
    {
        var stored = Buy(100000, 10);
        foreach (var installment in stored.Installments.Take(3))
            stored.StatementOf(installment).MarkAsPaid();

        ShouldFailWith(stored.Edit(100000, 2), "Já há 3 parcelas pagas; a compra não pode ter menos que isso.");
    }

    [Fact]
    public void Unpaid_installments_need_at_least_one_cent_each()
    {
        var stored = Buy(100000, 10);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();

        // 10000 já pagos: sobram 5 centavos para 9 parcelas.
        ShouldFailWith(stored.Edit(10005, 10), "O valor total deve ter ao menos 1 centavo por parcela não paga.");
    }

    [Fact]
    public void When_every_installment_is_paid_the_total_cannot_change()
    {
        var stored = Buy(30000, 3);
        foreach (var installment in stored.Installments)
            stored.StatementOf(installment).MarkAsPaid();

        ShouldFailWith(stored.Edit(31000, 3), "Todas as parcelas estão pagas; o valor total não pode mudar.");
        stored.Edit(30000, 3, description: "Notebook novo").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Installment_count_goes_from_1_to_24(int count) =>
        ShouldFailWith(Buy(30000, 3).Edit(30000, count), "O número de parcelas deve estar entre 1 e 24.");

    [Fact]
    public void Total_must_be_positive() =>
        ShouldFailWith(Buy(30000, 3).Edit(0, 3), "O valor deve ser maior que zero.");

    [Fact]
    public void A_purchase_can_become_a_single_payment()
    {
        var stored = Buy(30000, 3);

        stored.Edit(30000, 1).IsSuccess.ShouldBeTrue();

        var single = stored.Installments.ShouldHaveSingleItem();
        single.AmountCents.ShouldBe(30000);
        single.InstallmentNumber.ShouldBe(1);
    }

    [Fact]
    public void Description_and_category_change_on_unpaid_installments_only()
    {
        var stored = Buy(30000, 3);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();

        stored.Edit(30000, 3, Home, "  Geladeira ").IsSuccess.ShouldBeTrue();

        stored.Purchase.Description.ShouldBe("Geladeira");
        stored.Installments[0].Description.ShouldBe("Notebook");
        stored.Installments[0].CategoryId.ShouldBe(Electronics.Id);
        stored.Installments.Skip(1).ShouldAllBe(t => t.Description == "Geladeira" && t.CategoryId == Home.Id);
    }

    [Fact]
    public void Category_must_be_an_expense_category() =>
        ShouldFailWith(Buy(30000, 3).Edit(30000, 3, Salary),
            "A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

    [Fact]
    public void Failed_edit_changes_nothing()
    {
        var stored = Buy(30000, 3);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();

        // 10000 já pagos: sobra 1 centavo para 2 parcelas não pagas.
        stored.Edit(10001, 3, Home, "Outra").IsSuccess.ShouldBeFalse();

        stored.Purchase.TotalAmountCents.ShouldBe(30000);
        stored.Purchase.Description.ShouldBe("Notebook");
        stored.Installments.Select(t => t.AmountCents).ShouldBe([10000, 10000, 10000]);
        stored.Installments.ShouldAllBe(t => t.CategoryId == Electronics.Id);
    }

    // CLAUDE.md, regra 2: para qualquer compra, qualquer quantidade de faturas pagas e qualquer
    // edição válida, a soma das parcelas é o novo total e a numeração continua 1..n. A compra
    // original tem 2+ parcelas: à vista não existe InstallmentPurchase para editar.
    [Fact]
    public void Any_valid_edit_keeps_the_sum_exact() =>
        Gen.Select(Gen.Int[2, 24], Gen.Long[0, 1_000_000], Gen.Int[1, 24], Gen.Long[0, 1_000_000], Gen.Int[0, 24])
            .Sample((count, extra, newCount, newExtra, paidWanted) =>
            {
                var stored = Buy(count + extra, count);
                var paid = Math.Min(paidWanted, Math.Min(count, newCount));
                foreach (var installment in stored.Installments.Take(paid))
                    stored.StatementOf(installment).MarkAsPaid();
                var paidSum = stored.Installments.Take(paid).Sum(t => t.AmountCents);
                var newTotal = paidSum + (newCount - paid) + newExtra;
                if (newCount == paid) newTotal = paidSum;

                var result = stored.Edit(newTotal, newCount);

                return result.IsSuccess
                    && stored.Installments.Sum(t => t.AmountCents) == newTotal
                    && stored.Installments.All(t => t.AmountCents > 0)
                    && stored.Installments.Select(t => t.InstallmentNumber).OrderBy(n => n).SequenceEqual(Enumerable.Range(1, newCount).Select(n => (int?)n))
                    && stored.Purchase.TotalAmountCents == newTotal
                    && stored.Purchase.InstallmentCount == newCount;
            });
}
