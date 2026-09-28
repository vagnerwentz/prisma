using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Statements;

// docs/fase-2.md, 2.9, regras 4 e 5 (etapa 2.20): editar as datas de uma fatura move as compras cujo
// ciclo mudou, e o novo fechamento não atravessa o das vizinhas. Cartão "fecha 26, vence 5", como o do
// dono: a fatura 2026-10 fecha em 26/09, a 2026-11 em 26/10 e a 2026-12 em 26/11.
public sealed class StatementDateEditTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Account Card =
        Account.Create(UserId, "Itaú Visa", AccountType.CreditCard, 0, 26, 5, null).Value;

    private static readonly Account Checking =
        Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    private sealed class Ledger
    {
        public List<Statement> Statements { get; } = [];
        public List<Transaction> Transactions { get; } = [];

        public List<Transaction> Buy(DateOnly date, long total, int count = 1)
        {
            var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count,
                date, null, "Compra", Statements).Value;
            Statements.AddRange(created.OpenedStatements);
            Transactions.AddRange(created.Installments);
            return created.Installments.ToList();
        }

        public Transaction Refund(DateOnly date, long amount)
        {
            var created = Prisma.Domain.Transactions.Refund.Create(
                UserId, Card, amount, date, null, PaymentMethod.Credit, "Estorno", null, Statements).Value;
            Statements.AddRange(created.OpenedStatements);
            Transactions.Add(created.Refund);
            return created.Refund;
        }

        public void Move(IReadOnlyList<Transaction> purchase, StatementShift shift) =>
            Statements.AddRange(CardPurchase.MoveStatement(purchase, Card, Statements, shift).Value.Opened);

        public Result<StatementDateEdit> Edit(string reference, DateOnly closingDate, DateOnly dueDate)
        {
            var result = StatementEditing.EditDates(Card, Get(reference), Statements, Transactions, closingDate, dueDate);
            if (result.IsSuccess)
                Statements.AddRange(result.Value.Opened);
            return result;
        }

        public Statement Get(string reference) => Statements.Single(s => s.Reference == reference);

        public string ReferenceOf(Transaction transaction) => Statements.Single(s => s.Id == transaction.StatementId).Reference;

        // Como a lista de faturas: o estorno abate.
        public long TotalOf(string reference) =>
            Transactions.Where(t => t.StatementId == Get(reference).Id)
                .Sum(t => t.Type == TransactionType.Refund ? -t.AmountCents : t.AmountCents);
    }

    // O que o banco ajustou sem mudar o ciclo de ninguém: só o caixa acompanha o novo vencimento.
    [Fact]
    public void A_new_due_date_moves_only_the_settlement_of_the_purchases_that_stay()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 10), 4590).Single();

        var edit = ledger.Edit("2026-11", D(10, 26), D(11, 4)).Value;

        edit.Moved.ShouldBeEmpty();
        edit.MovedPurchases.ShouldBe(0);
        edit.Statement.ShouldBeSameAs(ledger.Get("2026-11"));
        ledger.Get("2026-11").DatesEditedManually.ShouldBeTrue();
        ledger.ReferenceOf(purchase).ShouldBe("2026-11");
        purchase.SettlementDate.ShouldBe(D(11, 4));
        purchase.PurchaseDate.ShouldBe(D(10, 10));
    }

    // Caso 5 da tabela.
    [Fact]
    public void A_later_closing_brings_the_purchase_of_that_day_back_from_the_next_statement()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 27), 4590).Single();
        ledger.Buy(D(10, 20), 1000);
        ledger.ReferenceOf(purchase).ShouldBe("2026-12");

        var edit = ledger.Edit("2026-11", D(10, 27), D(11, 5)).Value;

        edit.Moved.ShouldBe([purchase]);
        edit.MovedPurchases.ShouldBe(1);
        ledger.ReferenceOf(purchase).ShouldBe("2026-11");
        purchase.SettlementDate.ShouldBe(D(11, 5));
        purchase.PurchaseDate.ShouldBe(D(10, 27));
        ledger.TotalOf("2026-11").ShouldBe(5590);
        ledger.TotalOf("2026-12").ShouldBe(0);
    }

    // Caso 6 da tabela.
    [Fact]
    public void An_earlier_closing_sends_the_purchase_to_the_next_statement_opening_it()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 25), 4590).Single();

        var edit = ledger.Edit("2026-11", D(10, 24), D(11, 5)).Value;

        edit.Opened.Select(s => s.Reference).ShouldBe(["2026-12"]);
        ledger.ReferenceOf(purchase).ShouldBe("2026-12");
        purchase.SettlementDate.ShouldBe(D(12, 5));
        purchase.PurchaseDate.ShouldBe(D(10, 25));
        ledger.TotalOf("2026-11").ShouldBe(0);
    }

    // Caso 7 da tabela: a pessoa trouxe de volta a compra que a edição levou; a edição seguinte não a leva de novo.
    [Fact]
    public void A_pinned_purchase_stays_when_the_closing_changes()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 25), 4590);
        ledger.Edit("2026-11", D(10, 24), D(11, 5));
        ledger.Move(purchase, StatementShift.Previous);
        purchase.Single().StatementPinned.ShouldBeTrue();

        var edit = ledger.Edit("2026-11", D(10, 23), D(11, 4)).Value;

        edit.Moved.ShouldBeEmpty();
        ledger.ReferenceOf(purchase.Single()).ShouldBe("2026-11");
        purchase.Single().SettlementDate.ShouldBe(D(11, 4));
    }

    [Fact]
    public void An_installment_purchase_moves_whole_and_counts_as_one_purchase()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 25), 10000, count: 3);

        var edit = ledger.Edit("2026-11", D(10, 24), D(11, 5)).Value;

        edit.Moved.Count.ShouldBe(3);
        edit.MovedPurchases.ShouldBe(1);
        purchase.Select(ledger.ReferenceOf).ShouldBe(["2026-12", "2027-01", "2027-02"]);
        purchase.Select(t => t.AmountCents).ShouldBe([3334, 3333, 3333]);
        purchase.ShouldAllBe(t => t.PurchaseDate == D(10, 25));
    }

    [Fact]
    public void A_refund_on_the_card_follows_its_date()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 20), 5000);
        var refund = ledger.Refund(D(10, 27), 1000);
        ledger.ReferenceOf(refund).ShouldBe("2026-12");

        var edit = ledger.Edit("2026-11", D(10, 27), D(11, 5)).Value;

        edit.Moved.ShouldBe([refund]);
        ledger.ReferenceOf(refund).ShouldBe("2026-11");
        refund.SettlementDate.ShouldBe(D(11, 5));
        ledger.TotalOf("2026-11").ShouldBe(4000);
    }

    [Fact]
    public void A_statement_payment_never_moves()
    {
        var ledger = new Ledger();
        ledger.Buy(D(9, 20), 5000);
        var october = ledger.Get("2026-10");
        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 5000, D(10, 1), PaymentMethod.Pix, null).Value;
        ledger.Transactions.Add(legs.In);
        var (statementId, settlement) = (legs.In.StatementId, legs.In.SettlementDate);
        ledger.Buy(D(10, 25), 1000);

        ledger.Edit("2026-11", D(10, 24), D(11, 5)).IsSuccess.ShouldBeTrue();

        legs.In.StatementId.ShouldBe(statementId);
        legs.In.SettlementDate.ShouldBe(settlement);
    }

    // Caso 8 da tabela (regra 5).
    [Fact]
    public void The_closing_cannot_reach_the_closing_of_the_previous_statement()
    {
        var ledger = new Ledger();
        var september = ledger.Buy(D(9, 20), 1000).Single();
        var october = ledger.Buy(D(10, 20), 2000).Single();

        var result = ledger.Edit("2026-11", D(9, 26), D(11, 5));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("O fechamento tem de ser depois do fechamento da fatura anterior (26/09).");
        ledger.Get("2026-11").ClosingDate.ShouldBe(D(10, 26));
        ledger.Get("2026-11").DatesEditedManually.ShouldBeFalse();
        (ledger.ReferenceOf(september), ledger.ReferenceOf(october)).ShouldBe(("2026-10", "2026-11"));
        october.SettlementDate.ShouldBe(D(11, 5));
    }

    [Fact]
    public void The_closing_cannot_reach_the_closing_of_the_next_statement()
    {
        var ledger = new Ledger();
        ledger.Buy(D(9, 20), 1000);
        ledger.Buy(D(10, 20), 2000);

        var result = ledger.Edit("2026-10", D(10, 26), D(11, 5));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("O fechamento tem de ser antes do fechamento da fatura seguinte (26/10).");
        ledger.Get("2026-10").ClosingDate.ShouldBe(D(9, 26));
    }

    // Limites da ordem fechamento → vencimento → próximo fechamento: um dia depois do vencimento da
    // anterior (05/10) e um dia antes do fechamento da seguinte (26/11) valem.
    [Fact]
    public void A_day_after_the_previous_due_date_and_a_day_before_the_next_closing_are_accepted()
    {
        var ledger = new Ledger();
        ledger.Buy(D(9, 20), 1000);
        ledger.Buy(D(10, 20), 2000);
        ledger.Buy(D(11, 20), 3000);

        ledger.Edit("2026-11", D(10, 6), D(11, 5)).IsSuccess.ShouldBeTrue();
        ledger.Edit("2026-11", D(11, 25), D(11, 25)).IsSuccess.ShouldBeTrue();
    }

    // O giro a mais na roda do mês do iPhone: vencimento de novembro em 04/12 em vez de 04/11. Nenhuma
    // compra mudaria de fatura, mas o caixa de novembro inteiro iria para dezembro, em silêncio.
    [Fact]
    public void The_due_date_cannot_reach_the_closing_of_the_next_statement()
    {
        var ledger = new Ledger();
        var october = ledger.Buy(D(10, 20), 2000).Single();
        ledger.Buy(D(11, 20), 3000);

        ledger.Edit("2026-11", D(10, 26), D(12, 4)).Error.ShouldNotBeNull().Message
            .ShouldBe("O vencimento tem de ser antes do fechamento da fatura seguinte (26/11).");
        ledger.Edit("2026-11", D(10, 26), D(11, 26)).Error.ShouldNotBeNull().Message
            .ShouldBe("O vencimento tem de ser antes do fechamento da fatura seguinte (26/11).");

        ledger.Get("2026-11").DueDate.ShouldBe(D(11, 5));
        october.SettlementDate.ShouldBe(D(11, 5));
    }

    // O mesmo giro no fechamento: novembro fechando em 27/09 levaria um mês de compras para dezembro.
    [Fact]
    public void The_closing_cannot_come_before_the_due_date_of_the_previous_statement()
    {
        var ledger = new Ledger();
        ledger.Buy(D(9, 20), 1000);
        var october = ledger.Buy(D(10, 20), 2000).Single();

        ledger.Edit("2026-11", D(9, 27), D(11, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("O fechamento tem de ser depois do vencimento da fatura anterior (05/10).");
        ledger.Edit("2026-11", D(10, 5), D(11, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("O fechamento tem de ser depois do vencimento da fatura anterior (05/10).");

        ledger.ReferenceOf(october).ShouldBe("2026-11");
    }

    [Fact]
    public void A_neighbor_not_opened_yet_counts_with_its_calculated_due_date_and_closing()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 20), 2000);
        ledger.Statements.Select(s => s.Reference).ShouldBe(["2026-11"]);

        ledger.Edit("2026-11", D(10, 3), D(11, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("O fechamento tem de ser depois do vencimento da fatura anterior (05/10).");
        ledger.Edit("2026-11", D(10, 26), D(11, 30)).Error.ShouldNotBeNull().Message
            .ShouldBe("O vencimento tem de ser antes do fechamento da fatura seguinte (26/11).");
    }

    // A vizinha que ainda não existe conta com as datas que teria: a ordem das faturas nunca quebra.
    [Fact]
    public void A_neighbor_not_opened_yet_counts_with_its_calculated_closing()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 20), 2000);
        ledger.Statements.Select(s => s.Reference).ShouldBe(["2026-11"]);

        ledger.Edit("2026-11", D(9, 26), D(11, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("O fechamento tem de ser depois do fechamento da fatura anterior (26/09).");
        ledger.Edit("2026-11", D(11, 26), D(12, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("O fechamento tem de ser antes do fechamento da fatura seguinte (26/11).");
    }

    [Fact]
    public void A_paid_statement_keeps_its_dates_whatever_the_neighbors()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 20), 2000);
        ledger.Get("2026-11").MarkAsPaid();

        ledger.Edit("2026-11", D(9, 26), D(11, 5)).Error.ShouldNotBeNull().Message
            .ShouldBe("Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");
    }

    [Fact]
    public void The_due_date_still_cannot_come_before_the_closing()
    {
        var ledger = new Ledger();
        var purchase = ledger.Buy(D(10, 20), 2000).Single();

        ledger.Edit("2026-11", D(10, 26), D(10, 25)).Error.ShouldNotBeNull().Message
            .ShouldBe("O vencimento não pode ser antes do fechamento.");
        purchase.SettlementDate.ShouldBe(D(11, 5));
    }

    [Fact]
    public void A_statement_outside_the_card_list_is_a_programming_error()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 20), 2000);
        var loose = Statement.Open(UserId, Card.Id, StatementCalculator.ForReference("2027-03", 26, 5));

        Should.Throw<ArgumentException>(() =>
            StatementEditing.EditDates(Card, loose, ledger.Statements, ledger.Transactions, D(2, 26, 2027), D(3, 5, 2027)));
    }
}
