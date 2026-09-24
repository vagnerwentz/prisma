using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 1.10 (docs/fase-1.md, 2.3): transferência são duas transações Transfer ligadas pelo mesmo
// TransferPairId, fora de receita e despesa. Pagar a fatura é uma transferência para o cartão.
public sealed class TransferTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Oct10 = new(2026, 10, 10);

    private static Account Make(string name, AccountType type) =>
        type == AccountType.CreditCard
            ? Account.Create(UserId, name, type, 0, 26, 5, null).Value
            : Account.Create(UserId, name, type, 0, null, null, null).Value;

    private static readonly Account Checking = Make("Itaú", AccountType.Checking);
    private static readonly Account Cash = Make("Carteira", AccountType.Cash);
    private static readonly Account Investment = Make("Tesouro", AccountType.Investment);
    private static readonly Account Card = Make("Personnalité", AccountType.CreditCard);

    // Fatura de outubro: fecha 26/09, vence 05/10.
    private static Statement October() =>
        Statement.Open(UserId, Card.Id, new StatementDates("2026-10", new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 5)));

    private static void ShouldFailWith<T>(Result<T> result, ErrorType type, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(type);
        result.Error.Message.ShouldBe(message);
    }

    // --- Transferência livre ---

    [Fact]
    public void Transfer_creates_exactly_two_linked_transactions()
    {
        var legs = Transfer.Create(UserId, Checking, Investment, 50000, Oct10, PaymentMethod.Ted, "Aporte").Value;

        legs.Out.AccountId.ShouldBe(Checking.Id);
        legs.In.AccountId.ShouldBe(Investment.Id);
        legs.Out.TransferDirection.ShouldBe(TransferDirection.Out);
        legs.In.TransferDirection.ShouldBe(TransferDirection.In);
        legs.Out.TransferPairId.ShouldNotBeNull();
        legs.In.TransferPairId.ShouldBe(legs.Out.TransferPairId);
        new[] { legs.Out, legs.In }.ShouldAllBe(t =>
            t.Type == TransactionType.Transfer && t.AmountCents == 50000 && t.CategoryId == null &&
            t.PurchaseDate == Oct10 && t.SettlementDate == Oct10 && t.StatementId == null && t.Description == "Aporte");
    }

    [Fact]
    public void Each_transfer_gets_its_own_pair_id()
    {
        var first = Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Cash, null).Value;
        var second = Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Cash, null).Value;

        first.Out.TransferPairId.ShouldNotBe(second.Out.TransferPairId);
    }

    [Fact]
    public void Transfer_without_description_gets_a_default_one() =>
        Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Cash, "  ").Value.Out.Description.ShouldBe("Transferência");

    [Fact]
    public void Transfer_rules()
    {
        ShouldFailWith(Transfer.Create(UserId, Checking, Checking, 1000, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "Escolha contas diferentes para a transferência.");
        ShouldFailWith(Transfer.Create(UserId, Checking, Card, 1000, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "O cartão de crédito recebe dinheiro só pelo pagamento da fatura.");
        ShouldFailWith(Transfer.Create(UserId, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "O cartão de crédito recebe dinheiro só pelo pagamento da fatura.");
        ShouldFailWith(Transfer.Create(UserId, Checking, Cash, 0, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "O valor deve ser maior que zero.");
        ShouldFailWith(Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Credit, null),
            ErrorType.Validation, "Transferência não usa o meio de pagamento crédito.");
        ShouldFailWith(Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Pix, new string('x', 201)),
            ErrorType.Validation, "A descrição deve ter no máximo 200 caracteres.");
    }

    // --- Pagamento de fatura ---

    [Fact]
    public void Paying_a_statement_transfers_its_total_and_marks_it_paid()
    {
        var october = October();

        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 448286, Oct10, PaymentMethod.Boleto, null).Value;

        october.IsPaid.ShouldBeTrue();
        legs.Out.AccountId.ShouldBe(Checking.Id);
        legs.In.AccountId.ShouldBe(Card.Id);
        legs.In.StatementId.ShouldBe(october.Id);
        legs.Out.StatementId.ShouldBeNull();
        new[] { legs.Out, legs.In }.ShouldAllBe(t =>
            t.Type == TransactionType.Transfer && t.AmountCents == 448286 && t.SettlementDate == Oct10 &&
            t.Description == "Pagamento de fatura");
    }

    [Fact]
    public void Statement_is_paid_only_after_its_closing()
    {
        var october = October();

        ShouldFailWith(Transfer.PayStatement(UserId, october, Card, Checking, 1000, new DateOnly(2026, 9, 26), PaymentMethod.Pix, null),
            ErrorType.Validation, "A fatura só pode ser paga depois do fechamento (26/09/2026).");
        october.IsPaid.ShouldBeFalse();
        Transfer.PayStatement(UserId, october, Card, Checking, 1000, new DateOnly(2026, 9, 27), PaymentMethod.Pix, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Statement_payment_rules()
    {
        var october = October();

        ShouldFailWith(Transfer.PayStatement(UserId, october, Card, Card, 1000, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "A fatura é paga a partir de uma conta, não de um cartão.");
        ShouldFailWith(Transfer.PayStatement(UserId, october, Card, Checking, 0, Oct10, PaymentMethod.Pix, null),
            ErrorType.Validation, "Não há valor a pagar nesta fatura.");
        ShouldFailWith(Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Credit, null),
            ErrorType.Validation, "Transferência não usa o meio de pagamento crédito.");

        Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).IsSuccess.ShouldBeTrue();
        ShouldFailWith(Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null),
            ErrorType.Conflict, "Esta fatura já está paga.");
    }

    // --- Desfazer e restaurar ---

    [Fact]
    public void Removing_a_payment_marks_the_statement_unpaid()
    {
        var october = October();
        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).Value;

        Transfer.Remove(legs, october);

        october.IsPaid.ShouldBeFalse();
    }

    [Fact]
    public void Restoring_a_payment_marks_the_statement_paid_again_if_the_total_is_the_same()
    {
        var october = October();
        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).Value;
        Transfer.Remove(legs, october);

        Transfer.Restore(legs, october, statementTotalCents: 1000).ShouldBeNull();

        october.IsPaid.ShouldBeTrue();
    }

    [Fact]
    public void A_payment_is_not_restored_when_the_statement_changed()
    {
        var october = October();
        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).Value;
        Transfer.Remove(legs, october);

        var error = Transfer.Restore(legs, october, statementTotalCents: 1500);

        error!.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe("A fatura mudou desde o pagamento. Pague de novo pelo total atual.");
        october.IsPaid.ShouldBeFalse();
    }

    [Fact]
    public void A_payment_is_not_restored_over_another_payment()
    {
        var october = October();
        var first = Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).Value;
        Transfer.Remove(first, october);
        Transfer.PayStatement(UserId, october, Card, Checking, 1000, Oct10, PaymentMethod.Pix, null);

        Transfer.Restore(first, october, 1000)!.Message.ShouldBe("Esta fatura já está paga.");
    }

    // --- Transferência não é editada ---

    [Fact]
    public void Transfer_legs_are_not_edited()
    {
        const string message = "Transferência não é editada. Exclua e lance de novo.";
        var legs = Transfer.Create(UserId, Checking, Cash, 1000, Oct10, PaymentMethod.Pix, null).Value;
        var payment = Transfer.PayStatement(UserId, October(), Card, Checking, 1000, Oct10, PaymentMethod.Pix, null).Value;

        ShouldFailWith(legs.Out.UpdateSimple(Checking, TransactionType.Expense, 1000, Oct10, null, PaymentMethod.Pix, null),
            ErrorType.Validation, message);
        ShouldFailWith(CardPurchase.EditTransaction(payment.In, Card, [], Card, TransactionType.Transfer, 1000, Oct10, null, PaymentMethod.Pix, null),
            ErrorType.Validation, message);
    }
}
