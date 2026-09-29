using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/fase-2.md, 2.13 (etapa 2.24): trocar o cartão de uma compra é como trocar a data: a compra
// inteira vai para as faturas do cartão novo, pela regra dele, e o caixa segue o vencimento novo.
// Visa "fecha 26, vence 5"; Master "fecha 5, vence 12". Compra em 28/10/2026.
public sealed class CardPurchaseCardChangeTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly October28 = new(2026, 10, 28);

    private static readonly Account Visa = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, null).Value;
    private static readonly Account Master = Account.Create(UserId, "Master", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static readonly Account Checking = Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
    private static readonly Category Electronics = Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;

    private const string PaidSingle = "Esta compra está numa fatura paga. Desfaça o pagamento para trocar o cartão.";
    private const string PaidInstallments = "Há parcelas em fatura paga. Desfaça o pagamento para trocar o cartão.";
    private const string IntoPaid = "No cartão novo, a compra cairia numa fatura já paga.";
    private const string NotACard = "Compra no cartão só troca para outro cartão. Para usar outra conta, exclua e lance de novo.";

    // As faturas dos dois cartões numa lista só, como a API as carrega. A Visa já tem a fatura
    // 2026-11: a troca para o Master nunca pode reaproveitá-la (mesma referência, outro cartão).
    private sealed class Stored
    {
        public required CardPurchaseResult Created { get; init; }
        public required List<Statement> Statements { get; init; }
        public List<Transaction> Installments { get; } = [];

        public Transaction Single => Created.Installments.Single();
        public Statement StatementOf(Transaction t) => Statements.Single(s => s.Id == t.StatementId);

        public Statement Add(Account card, string reference, DateOnly closing, DateOnly due)
        {
            var statement = Statement.Open(UserId, card.Id, new StatementDates(reference, closing, due));
            Statements.Add(statement);
            return statement;
        }

        public Result<IReadOnlyList<Statement>> EditSingle(Account account, DateOnly? date = null)
        {
            var result = CardPurchase.EditTransaction(Single, Visa, Statements, account, TransactionType.Expense,
                Single.AmountCents, date ?? October28, Electronics, PaymentMethod.Credit, "Fone");
            if (result.IsSuccess)
                Statements.AddRange(result.Value);
            return result;
        }

        public Result<CardPurchaseEditResult> EditPurchase(Account account, DateOnly? date = null)
        {
            var purchase = Created.Purchase!;
            var result = CardPurchase.Edit(purchase, Visa, Installments, Statements, purchase.TotalAmountCents,
                purchase.InstallmentCount, Electronics, "Notebook", date ?? October28, account);
            if (result.IsSuccess)
                Statements.AddRange(result.Value.OpenedStatements);
            return result;
        }
    }

    private static Stored Buy(long total, int count)
    {
        var visaNovember = Statement.Open(UserId, Visa.Id,
            new StatementDates("2026-11", new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 5)));
        var created = CardPurchase.Create(UserId, Visa, TransactionType.Expense, PaymentMethod.Credit, total, count,
            October28, Electronics, "Notebook", [visaNovember]).Value;
        var stored = new Stored { Created = created, Statements = [visaNovember, .. created.OpenedStatements] };
        stored.Installments.AddRange(created.Installments);
        return stored;
    }

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    // --- Compra à vista ---

    [Fact]
    public void Single_purchase_goes_to_the_new_card_statement_and_settles_on_its_due_date()
    {
        var stored = Buy(8000, 1);
        stored.StatementOf(stored.Single).Reference.ShouldBe("2026-12"); // Visa: fecha 26/11, vence 05/12

        var opened = stored.EditSingle(Master).Value;

        stored.Single.AccountId.ShouldBe(Master.Id);
        var statement = stored.StatementOf(stored.Single);
        statement.AccountId.ShouldBe(Master.Id);
        statement.Reference.ShouldBe("2026-11");
        statement.ClosingDate.ShouldBe(new DateOnly(2026, 11, 5));
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 11, 12));
        stored.Single.PurchaseDate.ShouldBe(October28);
        stored.Single.AmountCents.ShouldBe(8000);
        opened.ShouldHaveSingleItem().ShouldBe(statement);
    }

    [Fact]
    public void Single_purchase_changes_card_and_date_together()
    {
        var stored = Buy(8000, 1);

        stored.EditSingle(Master, new DateOnly(2026, 11, 6)).IsSuccess.ShouldBeTrue();

        stored.Single.AccountId.ShouldBe(Master.Id);
        stored.StatementOf(stored.Single).Reference.ShouldBe("2026-12"); // Master: fecha 05/12, vence 12/12
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 12, 12));
        stored.Single.PurchaseDate.ShouldBe(new DateOnly(2026, 11, 6));
    }

    [Fact]
    public void Single_purchase_reuses_the_new_card_statement_that_already_exists()
    {
        var stored = Buy(8000, 1);
        var masterNovember = stored.Add(Master, "2026-11", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 12));

        stored.EditSingle(Master).Value.ShouldBeEmpty();

        stored.Single.StatementId.ShouldBe(masterNovember.Id);
    }

    // docs/fase-2.md, 2.13, regra 5: a compra foi presa a uma fatura da Visa, que não vale no Master.
    [Fact]
    public void Single_purchase_pinned_by_hand_is_released()
    {
        var stored = Buy(8000, 1);
        var moved = CardPurchase.MoveStatement([stored.Single], Visa, stored.Statements, StatementShift.Next).Value;
        stored.Statements.AddRange(moved.Opened);
        stored.Single.StatementPinned.ShouldBeTrue();

        stored.EditSingle(Master).IsSuccess.ShouldBeTrue();

        stored.StatementOf(stored.Single).Reference.ShouldBe("2026-11");
        stored.Single.StatementPinned.ShouldBeFalse();
    }

    [Fact]
    public void Single_purchase_in_a_paid_statement_keeps_its_card()
    {
        var stored = Buy(8000, 1);
        var visaDecember = stored.StatementOf(stored.Single);
        visaDecember.MarkAsPaid();

        ShouldFailWith(stored.EditSingle(Master), PaidSingle);

        stored.Single.AccountId.ShouldBe(Visa.Id);
        stored.Single.StatementId.ShouldBe(visaDecember.Id);
    }

    [Fact]
    public void Single_purchase_never_lands_in_a_paid_statement_of_the_new_card()
    {
        var stored = Buy(8000, 1);
        stored.Add(Master, "2026-11", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 12)).MarkAsPaid();
        var visaDecember = stored.Single.StatementId;

        ShouldFailWith(stored.EditSingle(Master), IntoPaid);

        stored.Single.AccountId.ShouldBe(Visa.Id);
        stored.Single.StatementId.ShouldBe(visaDecember);
    }

    [Fact]
    public void Single_purchase_only_changes_to_another_card()
    {
        var stored = Buy(8000, 1);

        ShouldFailWith(stored.EditSingle(Checking), NotACard);

        stored.Single.AccountId.ShouldBe(Visa.Id);
    }

    [Fact]
    public void Installment_card_changes_only_through_the_whole_purchase()
    {
        var stored = Buy(10000, 3);
        var second = stored.Installments[1];

        var result = CardPurchase.EditTransaction(second, Visa, stored.Statements, Master, TransactionType.Expense,
            second.AmountCents, October28, Electronics, PaymentMethod.Credit, "Notebook");

        ShouldFailWith(result, "O cartão de uma parcela muda pela compra inteira.");
        second.AccountId.ShouldBe(Visa.Id);
    }

    // --- Compra parcelada ---

    [Fact]
    public void Installment_purchase_changes_card_whole_with_the_same_amounts()
    {
        var stored = Buy(10000, 3);
        stored.Installments.Select(t => stored.StatementOf(t).Reference).ShouldBe(["2026-12", "2027-01", "2027-02"]);

        var result = stored.EditPurchase(Master);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Added.ShouldBeEmpty();
        result.Value.Removed.ShouldBeEmpty();
        var ordered = stored.Installments.OrderBy(t => t.InstallmentNumber).ToList();
        ordered.ShouldAllBe(t => t.AccountId == Master.Id && t.PurchaseDate == October28);
        ordered.Select(t => stored.StatementOf(t)).ShouldAllBe(s => s.AccountId == Master.Id);
        ordered.Select(t => stored.StatementOf(t).Reference).ShouldBe(["2026-11", "2026-12", "2027-01"]);
        ordered.Select(t => t.SettlementDate)
            .ShouldBe([new DateOnly(2026, 11, 12), new DateOnly(2026, 12, 12), new DateOnly(2027, 1, 12)]);
        ordered.Select(t => t.AmountCents).ShouldBe([3334L, 3333L, 3333L]);
        ordered.Sum(t => t.AmountCents).ShouldBe(10000);
        stored.Created.Purchase!.AccountId.ShouldBe(Master.Id);
    }

    [Fact]
    public void Installment_purchase_changes_card_and_date_together()
    {
        var stored = Buy(10000, 3);

        stored.EditPurchase(Master, new DateOnly(2026, 11, 6)).IsSuccess.ShouldBeTrue();

        stored.Installments.OrderBy(t => t.InstallmentNumber).Select(t => t.SettlementDate)
            .ShouldBe([new DateOnly(2026, 12, 12), new DateOnly(2027, 1, 12), new DateOnly(2027, 2, 12)]);
        stored.Installments.ShouldAllBe(t => t.AccountId == Master.Id && t.PurchaseDate == new DateOnly(2026, 11, 6));
    }

    [Fact]
    public void Installment_purchase_pinned_by_hand_is_released()
    {
        var stored = Buy(10000, 3);
        var moved = CardPurchase.MoveStatement(stored.Installments, Visa, stored.Statements, StatementShift.Next).Value;
        stored.Statements.AddRange(moved.Opened);

        stored.EditPurchase(Master).IsSuccess.ShouldBeTrue();

        stored.Installments.ShouldAllBe(t => !t.StatementPinned);
        stored.Installments.OrderBy(t => t.InstallmentNumber).Select(t => stored.StatementOf(t).Reference)
            .ShouldBe(["2026-11", "2026-12", "2027-01"]);
    }

    [Fact]
    public void Installment_purchase_with_a_paid_installment_keeps_its_card()
    {
        var stored = Buy(10000, 3);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();

        ShouldFailWith(stored.EditPurchase(Master), PaidInstallments);

        stored.Installments.ShouldAllBe(t => t.AccountId == Visa.Id);
        stored.Created.Purchase!.AccountId.ShouldBe(Visa.Id);
    }

    [Fact]
    public void Installment_purchase_never_lands_in_a_paid_statement_of_the_new_card()
    {
        var stored = Buy(10000, 3);
        stored.Add(Master, "2026-12", new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 12)).MarkAsPaid();

        ShouldFailWith(stored.EditPurchase(Master), IntoPaid);

        stored.Installments.ShouldAllBe(t => t.AccountId == Visa.Id);
        stored.Installments.Select(t => stored.StatementOf(t).AccountId).ShouldAllBe(id => id == Visa.Id);
    }

    [Fact]
    public void Installment_purchase_only_changes_to_another_card()
    {
        var stored = Buy(10000, 3);

        ShouldFailWith(stored.EditPurchase(Checking), NotACard);

        stored.Created.Purchase!.AccountId.ShouldBe(Visa.Id);
    }

    // docs/fase-2.md, 2.13, regra 4: a checagem que a API faz antes de editar a compra parcelada.
    [Fact]
    public void Installment_purchase_with_refunds_keeps_its_card()
    {
        var stored = Buy(10000, 3);

        var error = Refund.CheckPurchaseEdit(stored.Installments[0], TransactionType.Expense, Master.Id, 10000, refundedCents: 2000);

        error.ShouldNotBeNull().Message.ShouldBe("Esta despesa tem estornos; o tipo e a conta não mudam.");
    }
}
