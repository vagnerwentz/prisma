using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Statements;

// docs/fase-1.md, Statement: editar as datas recalcula o SettlementDate das transações dele. O que a
// edição move de fatura (etapa 2.20) está em StatementDateEditTests.
public sealed class StatementEditingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Account Card = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;

    [Fact]
    public void Editing_dates_moves_the_settlement_of_every_transaction_to_the_new_due_date()
    {
        var purchase = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 30000, 1,
            new DateOnly(2026, 3, 10), (Category?)null, "Notebook", []).Value;
        var other = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 4590, 1,
            new DateOnly(2026, 3, 20), (Category?)null, "Padaria", purchase.OpenedStatements).Value;
        var april = purchase.OpenedStatements.Single();
        var transactions = new[] { purchase.Installments[0], other.Installments[0] };

        // Itaú adiou o vencimento de 12/04 (domingo) para 13/04.
        var result = StatementEditing.EditDates(
            Card, april, [april], transactions, new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 13));

        result.IsSuccess.ShouldBeTrue();
        april.DueDate.ShouldBe(new DateOnly(2026, 4, 13));
        april.DatesEditedManually.ShouldBeTrue();
        transactions.ShouldAllBe(t => t.SettlementDate == new DateOnly(2026, 4, 13) && t.StatementId == april.Id);
        transactions.ShouldAllBe(t => t.PurchaseDate < new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void Invalid_dates_change_nothing()
    {
        var purchase = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 30000, 1,
            new DateOnly(2026, 3, 10), (Category?)null, null, []).Value;
        var april = purchase.OpenedStatements.Single();
        var transaction = purchase.Installments[0];

        var result = StatementEditing.EditDates(
            Card, april, [april], [transaction], new DateOnly(2026, 4, 13), new DateOnly(2026, 4, 12));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("O vencimento não pode ser antes do fechamento.");
        transaction.SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public void Transactions_of_another_card_are_a_programming_error()
    {
        var purchase = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 20000, 2,
            new DateOnly(2026, 3, 10), (Category?)null, null, []).Value;
        var april = purchase.OpenedStatements[0];
        var other = Account.Create(UserId, "Master", AccountType.CreditCard, 0, 5, 12, null).Value;
        var foreign = CardPurchase.Create(UserId, other, TransactionType.Expense, PaymentMethod.Credit, 1000, 1,
            new DateOnly(2026, 3, 10), (Category?)null, null, []).Value;

        Should.Throw<ArgumentException>(() => StatementEditing.EditDates(
            Card, april, purchase.OpenedStatements, [.. purchase.Installments, .. foreign.Installments],
            new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 13)));
    }

    [Fact]
    public void Marking_as_paid()
    {
        var statement = Statement.Open(UserId, Card.Id, new StatementDates("2026-04", new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 12)));

        statement.MarkAsPaid();

        statement.IsPaid.ShouldBeTrue();
    }
}
