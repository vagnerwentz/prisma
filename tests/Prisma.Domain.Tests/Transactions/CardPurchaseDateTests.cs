using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// Etapa 1.14b (docs/fase-1.md, 2.2): mudar a data da compra no cartão recalcula a fatura de cada
// parcela como na criação; os valores não mudam. Cartão que fecha dia 5 e vence dia 12.
public sealed class CardPurchaseDateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly March10 = new(2026, 3, 10);

    private static readonly Account Card = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static readonly Category Electronics = Category.Create(UserId, "Eletrônicos", TransactionType.Expense, null, null, null).Value;

    private const string PaidMessage = "Há parcelas em fatura paga; a data da compra não pode mudar.";
    private const string IntoPaidMessage = "A nova data leva parcelas para uma fatura já paga.";

    private sealed class Stored
    {
        public required CardPurchaseResult Created { get; init; }
        public required List<Statement> Statements { get; init; }
        public List<Transaction> Installments { get; } = [];

        public Transaction Single => Created.Installments.Single();
        public Statement StatementOf(Transaction t) => Statements.Single(s => s.Id == t.StatementId);

        public Result<CardPurchaseEditResult> EditPurchase(DateOnly date, int? count = null, long? total = null)
        {
            var result = CardPurchase.Edit(Created.Purchase!, Card, Installments, Statements,
                total ?? Created.Purchase!.TotalAmountCents, count ?? Created.Purchase!.InstallmentCount, Electronics, "Notebook", date);
            if (result.IsSuccess)
            {
                Installments.RemoveAll(t => result.Value.Removed.Contains(t));
                Installments.AddRange(result.Value.Added);
                Statements.AddRange(result.Value.OpenedStatements);
            }
            return result;
        }

        public Result<IReadOnlyList<Statement>> EditSingle(DateOnly date, long? amount = null)
        {
            var result = CardPurchase.EditTransaction(Single, Card, Statements, Card, TransactionType.Expense,
                amount ?? Single.AmountCents, date, Electronics, PaymentMethod.Credit, "Netflix");
            if (result.IsSuccess)
                Statements.AddRange(result.Value);
            return result;
        }
    }

    private static Stored Buy(long total, int count, DateOnly? date = null)
    {
        var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count,
            date ?? March10, Electronics, "Notebook", []).Value;
        var stored = new Stored { Created = created, Statements = created.OpenedStatements.ToList() };
        stored.Installments.AddRange(created.Installments);
        return stored;
    }

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    // --- Compra à vista no cartão ---

    [Fact]
    public void Single_purchase_moved_before_the_closing_goes_to_the_earlier_statement()
    {
        var stored = Buy(5590, 1);

        var opened = stored.EditSingle(new DateOnly(2026, 3, 4)).Value;

        stored.Single.PurchaseDate.ShouldBe(new DateOnly(2026, 3, 4));
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 3, 12));
        opened.ShouldHaveSingleItem().Reference.ShouldBe("2026-03");
        stored.StatementOf(stored.Single).Reference.ShouldBe("2026-03");
        stored.Single.AmountCents.ShouldBe(5590);
    }

    [Fact]
    public void Single_purchase_moved_to_the_closing_day_stays_in_the_closing_statement()
    {
        var stored = Buy(5590, 1);

        stored.EditSingle(new DateOnly(2026, 3, 5)).IsSuccess.ShouldBeTrue();

        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 3, 12));
    }

    [Fact]
    public void Single_purchase_moved_within_the_same_cycle_keeps_its_statement()
    {
        var stored = Buy(5590, 1);
        var statementId = stored.Single.StatementId;

        var opened = stored.EditSingle(new DateOnly(2026, 3, 20)).Value;

        opened.ShouldBeEmpty();
        stored.Single.StatementId.ShouldBe(statementId);
        stored.Single.PurchaseDate.ShouldBe(new DateOnly(2026, 3, 20));
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public void Single_purchase_reuses_an_existing_statement_and_its_edited_dates()
    {
        var stored = Buy(5590, 1);
        var march = Statement.Open(UserId, Card.Id, new StatementDates("2026-03", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12)));
        march.EditDates(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 13));
        stored.Statements.Add(march);

        // Com o fechamento adiado para 06/03, uma compra em 06/03 ainda cai na fatura de março.
        var opened = stored.EditSingle(new DateOnly(2026, 3, 6)).Value;

        opened.ShouldBeEmpty();
        stored.Single.StatementId.ShouldBe(march.Id);
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 3, 13));
    }

    [Fact]
    public void Single_purchase_date_and_amount_change_together()
    {
        var stored = Buy(5590, 1);

        stored.EditSingle(new DateOnly(2026, 5, 10), amount: 6590).IsSuccess.ShouldBeTrue();

        stored.Single.AmountCents.ShouldBe(6590);
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 6, 12));
    }

    [Fact]
    public void Single_purchase_in_a_paid_statement_keeps_its_date()
    {
        var stored = Buy(5590, 1);
        stored.StatementOf(stored.Single).MarkAsPaid();

        ShouldFailWith(stored.EditSingle(new DateOnly(2026, 5, 10)), PaidMessage);
        stored.Single.PurchaseDate.ShouldBe(March10);
    }

    [Fact]
    public void Single_purchase_cannot_move_into_a_paid_statement()
    {
        var stored = Buy(5590, 1);
        var march = Statement.Open(UserId, Card.Id, new StatementDates("2026-03", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12)));
        march.MarkAsPaid();
        stored.Statements.Add(march);

        ShouldFailWith(stored.EditSingle(new DateOnly(2026, 3, 4)), IntoPaidMessage);
        stored.Single.PurchaseDate.ShouldBe(March10);
        stored.Single.SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public void Installment_date_changes_only_through_the_whole_purchase()
    {
        var stored = Buy(30000, 3);
        var second = stored.Installments[1];

        var result = CardPurchase.EditTransaction(second, Card, stored.Statements, Card, TransactionType.Expense,
            second.AmountCents, new DateOnly(2026, 5, 10), Electronics, PaymentMethod.Credit, "Notebook");

        ShouldFailWith(result, "A data de uma parcela muda pela compra inteira.");
    }

    [Fact]
    public void Account_type_and_method_still_never_change()
    {
        const string message = "Em compra no cartão, conta, tipo e meio de pagamento não mudam. Exclua e lance de novo.";
        var single = Buy(5590, 1).Single;
        var other = Account.Create(UserId, "Master", AccountType.CreditCard, 0, 5, 12, null).Value;

        ShouldFailWith(CardPurchase.EditTransaction(single, Card, [], other, TransactionType.Expense, 5590, March10, null, PaymentMethod.Credit, null), message);
        ShouldFailWith(CardPurchase.EditTransaction(single, Card, [], Card, TransactionType.Income, 5590, March10, null, PaymentMethod.Credit, null), message);
        ShouldFailWith(CardPurchase.EditTransaction(single, Card, [], Card, TransactionType.Expense, 5590, March10, null, PaymentMethod.Pix, null), message);
    }

    // --- Compra parcelada ---

    [Fact]
    public void Installments_move_together_and_keep_their_amounts()
    {
        var stored = Buy(100000, 3);
        var amounts = stored.Installments.Select(t => t.AmountCents).ToList();

        var result = stored.EditPurchase(new DateOnly(2026, 5, 10));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Added.ShouldBeEmpty();
        result.Value.Removed.ShouldBeEmpty();
        var ordered = stored.Installments.OrderBy(t => t.InstallmentNumber).ToList();
        ordered.Select(t => t.SettlementDate).ShouldBe([new DateOnly(2026, 6, 12), new DateOnly(2026, 7, 12), new DateOnly(2026, 8, 12)]);
        ordered.Select(t => t.AmountCents).ShouldBe(amounts);
        ordered.Sum(t => t.AmountCents).ShouldBe(100000);
        ordered.ShouldAllBe(t => t.PurchaseDate == new DateOnly(2026, 5, 10));
        ordered.Select(t => stored.StatementOf(t).Reference).ShouldBe(["2026-06", "2026-07", "2026-08"]);
        stored.Created.Purchase!.PurchaseDate.ShouldBe(new DateOnly(2026, 5, 10));
    }

    [Fact]
    public void Moving_earlier_reuses_the_statements_it_overlaps_and_opens_the_missing_ones()
    {
        var stored = Buy(30000, 3); // abril, maio, junho

        var result = stored.EditPurchase(new DateOnly(2026, 3, 4)); // março, abril, maio

        result.Value.OpenedStatements.Select(s => s.Reference).ShouldBe(["2026-03"]);
        stored.Installments.OrderBy(t => t.InstallmentNumber).Select(t => t.SettlementDate)
            .ShouldBe([new DateOnly(2026, 3, 12), new DateOnly(2026, 4, 12), new DateOnly(2026, 5, 12)]);
    }

    [Fact]
    public void Date_and_installment_count_change_together()
    {
        var stored = Buy(30000, 3);

        var result = stored.EditPurchase(new DateOnly(2026, 3, 4), count: 5);

        result.IsSuccess.ShouldBeTrue();
        var ordered = stored.Installments.OrderBy(t => t.InstallmentNumber).ToList();
        ordered.Select(t => t.SettlementDate).ShouldBe(Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 3, 12).AddMonths(i)));
        ordered.ShouldAllBe(t => t.PurchaseDate == new DateOnly(2026, 3, 4) && t.AmountCents == 6000);
    }

    [Fact]
    public void Same_date_keeps_every_statement()
    {
        var stored = Buy(30000, 3);
        var statementIds = stored.Installments.Select(t => t.StatementId).ToList();

        stored.EditPurchase(March10).Value.OpenedStatements.ShouldBeEmpty();

        stored.Installments.Select(t => t.StatementId).ShouldBe(statementIds);
    }

    [Fact]
    public void Purchase_with_a_paid_installment_keeps_its_date()
    {
        var stored = Buy(30000, 3);
        stored.StatementOf(stored.Installments[0]).MarkAsPaid();

        ShouldFailWith(stored.EditPurchase(new DateOnly(2026, 5, 10)), PaidMessage);
        stored.Installments.ShouldAllBe(t => t.PurchaseDate == March10);
        stored.Created.Purchase!.PurchaseDate.ShouldBe(March10);
    }

    [Fact]
    public void Purchase_cannot_move_installments_into_a_paid_statement()
    {
        var stored = Buy(30000, 3);
        var march = Statement.Open(UserId, Card.Id, new StatementDates("2026-03", new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12)));
        march.MarkAsPaid();
        stored.Statements.Add(march);

        ShouldFailWith(stored.EditPurchase(new DateOnly(2026, 3, 4)), IntoPaidMessage);
        stored.Installments.ShouldAllBe(t => t.PurchaseDate == March10);
        stored.Installments.OrderBy(t => t.InstallmentNumber).First().SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }
}
