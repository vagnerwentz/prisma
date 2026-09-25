using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 2.5 (docs/fase-2.md, 2.5), com o exemplo da regra: Visa fecha dia 26 e vence dia 5.
public sealed class RefundTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Account Visa =
        Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, 500000).Value;

    private static readonly Account Itau =
        Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;

    private static readonly Account Treasury =
        Account.Create(UserId, "Tesouro", AccountType.Investment, 0, null, null, null).Value;

    private static readonly Category Restaurant =
        Category.Create(UserId, "Restaurante", TransactionType.Expense, null, null, null).Value;

    private static readonly Category Salary =
        Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    // O jantar de 20/09 no Visa, na fatura que vence em 05/10.
    private static (Transaction Dinner, Statement October) Dinner()
    {
        var purchase = CardPurchase.Create(UserId, Visa, TransactionType.Expense, PaymentMethod.Credit, 60000, 1,
            new DateOnly(2026, 9, 20), Restaurant, "Jantar", []).Value;
        return (purchase.Installments[0], purchase.OpenedStatements[0]);
    }

    private static Result<RefundResult> Create(
        Account account, long cents, string date, IReadOnlyCollection<Statement>? statements = null,
        RefundTarget? target = null, PaymentMethod? method = null, Category? category = null, string? description = "Estorno") =>
        Refund.Create(UserId, account, cents, DateOnly.Parse(date), category, method ?? (account == Visa ? PaymentMethod.Credit : PaymentMethod.Pix),
            description, target, statements ?? []);

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe(message);
    }

    [Fact]
    public void A_card_refund_lands_on_the_statement_open_on_its_date_not_on_the_purchase_statement()
    {
        var (dinner, october) = Dinner();
        october.MarkAsPaid();

        var result = Create(Visa, 15000, "2026-10-10", [october], new RefundTarget(dinner, 60000), category: Restaurant).Value;

        var refund = result.Refund;
        refund.Type.ShouldBe(TransactionType.Refund);
        refund.AmountCents.ShouldBe(15000);
        refund.PurchaseDate.ShouldBe(new DateOnly(2026, 10, 10));
        refund.SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
        refund.RefundedTransactionId.ShouldBe(dinner.Id);
        refund.CategoryId.ShouldBe(Restaurant.Id);
        result.OpenedStatements.ShouldHaveSingleItem().Reference.ShouldBe("2026-11");
        refund.StatementId.ShouldBe(result.OpenedStatements[0].Id);
    }

    [Fact]
    public void A_card_refund_reuses_the_statement_that_already_exists()
    {
        var shoes = CardPurchase.Create(UserId, Visa, TransactionType.Expense, PaymentMethod.Credit, 30000, 1,
            new DateOnly(2026, 10, 10), null, "Sapato", []).Value;
        var november = shoes.OpenedStatements[0];

        var result = Create(Visa, 4000, "2026-10-12", [november]).Value;

        result.OpenedStatements.ShouldBeEmpty();
        result.Refund.StatementId.ShouldBe(november.Id);
    }

    [Fact]
    public void A_refund_on_the_cycle_of_a_paid_statement_is_refused()
    {
        var (_, october) = Dinner();
        october.MarkAsPaid();

        ShouldFailWith(Create(Visa, 1000, "2026-09-25", [october]),
            "Esta fatura já está paga. Lance o estorno com a data em que ele apareceu no cartão.");
    }

    [Fact]
    public void An_account_refund_settles_on_its_own_date()
    {
        var refund = Create(Itau, 5000, "2026-10-06").Value.Refund;

        refund.SettlementDate.ShouldBe(new DateOnly(2026, 10, 6));
        refund.StatementId.ShouldBeNull();
        refund.Method.ShouldBe(PaymentMethod.Pix);
    }

    [Fact]
    public void Account_method_amount_and_category_are_checked()
    {
        ShouldFailWith(Create(Treasury, 1000, "2026-10-06"), "Estorno vai para a conta ou o cartão em que a compra foi feita.");
        ShouldFailWith(Create(Visa, 1000, "2026-10-06", method: PaymentMethod.Pix), "No cartão, o estorno usa o meio de pagamento crédito.");
        ShouldFailWith(Create(Itau, 1000, "2026-10-06", method: PaymentMethod.Credit), "Pagamento no crédito exige uma conta de cartão de crédito.");
        ShouldFailWith(Create(Itau, 0, "2026-10-06"), "O valor deve ser maior que zero.");
        ShouldFailWith(Create(Itau, 1000, "2026-10-06", category: Salary), "A categoria do estorno deve ser de despesa.");
        ShouldFailWith(Create(Itau, 1000, "2026-10-06", description: new string('x', 201)), "A descrição deve ter no máximo 200 caracteres.");
    }

    [Fact]
    public void A_linked_refund_stays_on_the_purchase_account_and_needs_an_expense()
    {
        var (dinner, _) = Dinner();
        var salary = Transaction.CreateSimple(UserId, Itau, TransactionType.Income, 800000, new DateOnly(2026, 10, 5),
            Salary, PaymentMethod.Pix, "Salário").Value;

        ShouldFailWith(Create(Itau, 1000, "2026-10-10", target: new RefundTarget(dinner, 60000)),
            "O estorno fica na mesma conta da compra.");
        ShouldFailWith(Create(Itau, 1000, "2026-10-10", target: new RefundTarget(salary, 800000)),
            "Só uma despesa pode ser estornada.");
    }

    [Fact]
    public void Linked_refunds_never_exceed_the_purchase()
    {
        var (dinner, _) = Dinner();
        var refundable = Refund.RefundableCents(60000, alreadyRefundedCents: 15000);
        refundable.ShouldBe(45000);

        ShouldFailWith(Create(Visa, 50000, "2026-10-12", target: new RefundTarget(dinner, refundable)),
            "O estorno passa do valor da compra. Restam R$ 450,00 para estornar.");
        Create(Visa, 45000, "2026-10-12", target: new RefundTarget(dinner, refundable)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_left_to_refund()
    {
        var (dinner, _) = Dinner();

        ShouldFailWith(Create(Visa, 1, "2026-10-12", target: new RefundTarget(dinner, 0)),
            "Esta compra já foi estornada por inteiro.");
    }

    [Fact]
    public void Editing_moves_a_card_refund_to_the_statement_of_the_new_date()
    {
        var created = Create(Visa, 4000, "2026-10-12").Value;
        var refund = created.Refund;

        var opened = Refund.Edit(refund, Visa, 3000, new DateOnly(2026, 11, 3), null, PaymentMethod.Credit, "Estorno",
            refundableCents: null, created.OpenedStatements).Value;

        refund.AmountCents.ShouldBe(3000);
        refund.SettlementDate.ShouldBe(new DateOnly(2026, 12, 5));
        opened.ShouldHaveSingleItem().Reference.ShouldBe("2026-12");
    }

    [Fact]
    public void Editing_keeps_the_account_and_respects_paid_statements_and_the_limit()
    {
        var (dinner, october) = Dinner();
        var created = Create(Visa, 15000, "2026-10-10", [october], new RefundTarget(dinner, 60000)).Value;
        var refund = created.Refund;
        IReadOnlyCollection<Statement> statements = [october, created.OpenedStatements[0]];
        october.MarkAsPaid();

        ShouldFailWith(Refund.Edit(refund, Itau, 15000, refund.PurchaseDate, null, PaymentMethod.Pix, "", 60000, statements),
            "No estorno, a conta não muda. Exclua e lance de novo.");
        ShouldFailWith(Refund.Edit(refund, Visa, 15000, new DateOnly(2026, 9, 25), null, PaymentMethod.Credit, "", 60000, statements),
            "A nova data leva o estorno para uma fatura já paga.");
        ShouldFailWith(Refund.Edit(refund, Visa, 60001, refund.PurchaseDate, null, PaymentMethod.Credit, "", 60000, statements),
            "O estorno passa do valor da compra. Restam R$ 600,00 para estornar.");

        created.OpenedStatements[0].MarkAsPaid();
        ShouldFailWith(Refund.Edit(refund, Visa, 10000, refund.PurchaseDate, null, PaymentMethod.Credit, "", 60000, statements),
            "Este estorno está numa fatura paga; valor e data não mudam. Desfaça o pagamento para alterá-los.");
    }

    [Fact]
    public void Restoring_checks_the_paid_statement_and_the_limit()
    {
        var (dinner, october) = Dinner();
        var created = Create(Visa, 15000, "2026-10-10", [october], new RefundTarget(dinner, 60000)).Value;
        var november = created.OpenedStatements[0];

        Refund.CheckCanRestore(created.Refund, 15000, [november]).ShouldBeNull();
        Refund.CheckCanRestore(created.Refund, 10000, [november])!.Message
            .ShouldBe("O estorno passa do valor da compra. Restam R$ 100,00 para estornar.");

        november.MarkAsPaid();
        Refund.CheckCanRestore(created.Refund, 15000, [november])!.Message
            .ShouldBe("A fatura deste estorno já está paga. Desfaça o pagamento para restaurá-lo.");
    }

    [Theory]
    [InlineData(15000, 15000, true)]
    [InlineData(14999, 15000, false)]
    [InlineData(100, 0, true)]
    public void A_purchase_cannot_go_below_what_was_already_refunded(long newAmount, long refunded, bool allowed)
    {
        var error = Refund.CheckPurchaseKeepsRefunds(newAmount, refunded);

        if (allowed) error.ShouldBeNull();
        else error!.Message.ShouldBe("Já foram estornados R$ 150,00 desta compra; o valor não pode ficar abaixo disso.");
    }

    [Fact]
    public void A_refunded_purchase_keeps_its_type_and_account()
    {
        var (dinner, _) = Dinner();

        Refund.CheckPurchaseEdit(dinner, TransactionType.Expense, Visa.Id, 60000, refundedCents: 15000).ShouldBeNull();
        Refund.CheckPurchaseEdit(dinner, TransactionType.Income, Visa.Id, 60000, 15000)!.Message
            .ShouldBe("Esta despesa tem estornos; o tipo e a conta não mudam.");
        Refund.CheckPurchaseEdit(dinner, TransactionType.Expense, Itau.Id, 60000, 15000)!.Message
            .ShouldBe("Esta despesa tem estornos; o tipo e a conta não mudam.");
        Refund.CheckPurchaseEdit(dinner, TransactionType.Expense, Visa.Id, 10000, 15000)!.Message
            .ShouldBe("Já foram estornados R$ 150,00 desta compra; o valor não pode ficar abaixo disso.");
        Refund.CheckPurchaseEdit(dinner, TransactionType.Income, Itau.Id, 100, refundedCents: 0).ShouldBeNull();
    }
}
