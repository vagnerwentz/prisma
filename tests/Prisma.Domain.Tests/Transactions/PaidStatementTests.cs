using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 1.10 (docs/fase-1.md, 2.3): fatura paga não muda de valor. Nada entra, sai ou muda de
// valor nela; descrição e categoria continuam editáveis. Cartão que fecha dia 5 e vence dia 12.
public sealed class PaidStatementTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Card = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static readonly Category Electronics = Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;
    private static readonly Category Home = Category.Create(UserId, "Casa", TransactionType.Expense, null, null, null).Value;

    private static CardPurchaseResult Buy(long total, int count, IReadOnlyCollection<Statement>? existing = null, DateOnly? date = null) =>
        CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count, date ?? March10,
            Electronics, "Notebook", existing ?? []).Value;

    private static Result<CardPurchaseResult> TryBuy(long total, int count, IReadOnlyCollection<Statement> existing, DateOnly? date = null) =>
        CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count, date ?? March10,
            Electronics, "Fone", existing);

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Message.ShouldBe(message);
    }

    // --- Compra nova ---

    [Fact]
    public void A_purchase_into_a_paid_statement_is_refused()
    {
        var april = Buy(1000, 1).OpenedStatements.Single();
        april.MarkAsPaid();

        ShouldFailWith(TryBuy(5000, 1, [april]), "Esta compra cai numa fatura já paga. Desfaça o pagamento para lançá-la.");
    }

    [Fact]
    public void An_installment_purchase_touching_a_paid_statement_is_refused()
    {
        var statements = Buy(3000, 3).OpenedStatements.ToList(); // abril, maio, junho
        statements[1].MarkAsPaid();                               // maio paga

        // Compra de 10/02 em 3x: março, abril e maio.
        ShouldFailWith(TryBuy(3000, 3, statements, new DateOnly(2026, 2, 10)),
            "Esta compra cai numa fatura já paga. Desfaça o pagamento para lançá-la.");
    }

    [Fact]
    public void A_purchase_after_the_paid_statement_is_accepted()
    {
        var april = Buy(1000, 1).OpenedStatements.Single();
        april.MarkAsPaid();

        TryBuy(5000, 1, [april], new DateOnly(2026, 4, 6)).IsSuccess.ShouldBeTrue();
    }

    // --- Edição ---

    [Fact]
    public void Amount_of_a_purchase_in_a_paid_statement_does_not_change()
    {
        var bought = Buy(1000, 1);
        bought.OpenedStatements.Single().MarkAsPaid();
        var single = bought.Installments.Single();

        ShouldFailWith(
            CardPurchase.EditTransaction(single, Card, bought.OpenedStatements, Card, TransactionType.Expense, 2000, March10, Electronics, PaymentMethod.Credit, "Notebook"),
            "Esta compra está numa fatura paga; o valor não muda. Desfaça o pagamento para alterá-lo.");
        single.AmountCents.ShouldBe(1000);
    }

    [Fact]
    public void Description_and_category_of_a_purchase_in_a_paid_statement_still_change()
    {
        var bought = Buy(1000, 1);
        bought.OpenedStatements.Single().MarkAsPaid();
        var single = bought.Installments.Single();

        CardPurchase.EditTransaction(single, Card, bought.OpenedStatements, Card, TransactionType.Expense, 1000, March10, Home, PaymentMethod.Credit, "Notebook usado")
            .IsSuccess.ShouldBeTrue();

        single.CategoryId.ShouldBe(Home.Id);
        single.Description.ShouldBe("Notebook usado");
    }

    [Fact]
    public void New_installments_into_a_paid_statement_are_refused()
    {
        var bought = Buy(3000, 3);
        var statements = bought.OpenedStatements.ToList();
        var july = Statement.Open(UserId, Card.Id, new StatementDates("2026-07", new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 12)));
        july.MarkAsPaid();
        statements.Add(july);

        ShouldFailWith(
            CardPurchase.Edit(bought.Purchase!, Card, bought.Installments, statements, 4000, 4, Electronics, "Notebook", March10),
            "As novas parcelas cairiam numa fatura já paga. Desfaça o pagamento para aumentar as parcelas.");
    }

    [Fact]
    public void Dates_of_a_paid_statement_do_not_change()
    {
        var bought = Buy(1000, 1);
        var april = bought.OpenedStatements.Single();
        april.MarkAsPaid();

        var result = StatementEditing.EditDates(april, bought.Installments, new DateOnly(2026, 4, 3), new DateOnly(2026, 4, 10));

        ShouldFailWith(result, "Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");
        april.DueDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    // --- Excluir e restaurar ---

    [Fact]
    public void A_purchase_in_a_paid_statement_is_not_removed()
    {
        var bought = Buy(3000, 3);
        bought.OpenedStatements[0].MarkAsPaid();

        CardPurchase.CheckCanRemove(bought.Installments, bought.OpenedStatements)!.Message
            .ShouldBe("Esta compra está numa fatura paga. Desfaça o pagamento para excluí-la.");
    }

    [Fact]
    public void A_purchase_without_paid_statements_is_removed()
    {
        var bought = Buy(3000, 3);

        CardPurchase.CheckCanRemove(bought.Installments, bought.OpenedStatements).ShouldBeNull();
    }

    [Fact]
    public void A_purchase_is_not_restored_into_a_paid_statement()
    {
        var bought = Buy(3000, 3);
        bought.OpenedStatements[2].MarkAsPaid();

        CardPurchase.CheckCanRestore(bought.Installments, bought.OpenedStatements)!.Message
            .ShouldBe("A fatura desta compra já está paga. Desfaça o pagamento para restaurá-la.");
        ShouldFailWith(CardPurchase.Restore(bought.Purchase!, bought.Installments, new HashSet<Guid>(), bought.OpenedStatements),
            "A fatura desta compra já está paga. Desfaça o pagamento para restaurá-la.");
    }
}
