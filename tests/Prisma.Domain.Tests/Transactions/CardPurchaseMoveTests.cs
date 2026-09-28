using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/fase-2.md, 2.9, regras 2 e 3 (etapa 2.20): mover uma compra para a fatura seguinte ou
// anterior. Cartão "fecha 26, vence 5", como o do dono: a fatura 2026-10 fecha em 26/09 e vence em
// 05/10; a 2026-11 fecha em 26/10 e vence em 05/11.
public sealed class CardPurchaseMoveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Account Card =
        Account.Create(UserId, "Itaú Visa", AccountType.CreditCard, 0, 26, 5, null).Value;

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    private sealed class Ledger
    {
        public List<Statement> Statements { get; } = [];
        public List<Transaction> Transactions { get; } = [];

        public List<Transaction> Buy(DateOnly date, long total, int count = 1)
        {
            var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, total, count,
                date, null, "Lanche", Statements).Value;
            Statements.AddRange(created.OpenedStatements);
            Transactions.AddRange(created.Installments);
            return created.Installments.ToList();
        }

        public Result<StatementMove> Move(IReadOnlyList<Transaction> purchase, StatementShift shift)
        {
            var result = CardPurchase.MoveStatement(purchase, Card, Statements, shift);
            if (result.IsSuccess)
                Statements.AddRange(result.Value.Opened);
            return result;
        }

        public Statement Get(string reference) => Statements.Single(s => s.Reference == reference);

        public string ReferenceOf(Transaction transaction) => Statements.Single(s => s.Id == transaction.StatementId).Reference;

        public long TotalOf(string reference) =>
            Transactions.Where(t => t.StatementId == Get(reference).Id).Sum(t => t.AmountCents);
    }

    // Caso 1 da tabela: os lanches de 25/09/2026 foram para a fatura de novembro no Itaú.
    [Fact]
    public void The_snacks_move_to_the_next_statement_keeping_the_purchase_date()
    {
        var ledger = new Ledger();
        var first = ledger.Buy(D(9, 25), 3000);
        var second = ledger.Buy(D(9, 25), 2550);
        ledger.Buy(D(9, 10), 10000);
        ledger.TotalOf("2026-10").ShouldBe(15550);

        var moved = ledger.Move(first, StatementShift.Next).Value;
        ledger.Move(second, StatementShift.Next);

        moved.Opened.Select(s => s.Reference).ShouldBe(["2026-11"]);
        ledger.Get("2026-11").ClosingDate.ShouldBe(D(10, 26));
        foreach (var snack in first.Concat(second))
        {
            ledger.ReferenceOf(snack).ShouldBe("2026-11");
            snack.SettlementDate.ShouldBe(D(11, 5));
            snack.PurchaseDate.ShouldBe(D(9, 25));
            snack.StatementPinned.ShouldBeTrue();
        }
        ledger.TotalOf("2026-10").ShouldBe(10000);
        ledger.TotalOf("2026-11").ShouldBe(5550);
    }

    // Caso 2 da tabela: voltar para a fatura que a previsão daria solta a compra (regra 3).
    [Fact]
    public void Moving_back_to_the_calculated_statement_unpins_the_purchase()
    {
        var ledger = new Ledger();
        var snack = ledger.Buy(D(9, 25), 3000);
        ledger.Move(snack, StatementShift.Next);

        ledger.Move(snack, StatementShift.Previous).IsSuccess.ShouldBeTrue();

        ledger.ReferenceOf(snack.Single()).ShouldBe("2026-10");
        snack.Single().SettlementDate.ShouldBe(D(10, 5));
        snack.Single().StatementPinned.ShouldBeFalse();
    }

    // Caso 3 da tabela: a parcelada anda inteira, cada parcela um ciclo, a soma igual.
    [Fact]
    public void An_installment_purchase_moves_whole()
    {
        var ledger = new Ledger();
        var installments = ledger.Buy(D(9, 20), 10000, count: 3);
        installments.Select(ledger.ReferenceOf).ShouldBe(["2026-10", "2026-11", "2026-12"]);

        ledger.Move(installments, StatementShift.Next).IsSuccess.ShouldBeTrue();

        installments.Select(ledger.ReferenceOf).ShouldBe(["2026-11", "2026-12", "2027-01"]);
        installments.Select(t => t.AmountCents).ShouldBe([3334L, 3333L, 3333L]);
        installments.Select(t => t.SettlementDate).ShouldBe([D(11, 5), D(12, 5), D(1, 5, 2027)]);
        installments.ShouldAllBe(t => t.StatementPinned && t.PurchaseDate == D(9, 20));
    }

    // Caso 4 da tabela: parcela em fatura paga, recusado sem mudar nada.
    [Fact]
    public void A_purchase_with_an_installment_in_a_paid_statement_is_refused()
    {
        var ledger = new Ledger();
        var installments = ledger.Buy(D(9, 20), 10000, count: 3);
        ledger.Get("2026-10").MarkAsPaid();

        var result = ledger.Move(installments, StatementShift.Next);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("Esta compra está numa fatura paga. Desfaça o pagamento para mudá-la de fatura.");
        installments.Select(ledger.ReferenceOf).ShouldBe(["2026-10", "2026-11", "2026-12"]);
        installments.ShouldAllBe(t => !t.StatementPinned);
    }

    [Fact]
    public void Moving_into_a_paid_statement_is_refused_and_opens_nothing()
    {
        var ledger = new Ledger();
        var snack = ledger.Buy(D(10, 20), 3000);
        ledger.Buy(D(9, 20), 1000);
        ledger.Get("2026-10").MarkAsPaid();

        var result = ledger.Move(snack, StatementShift.Previous);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("A fatura anterior já está paga.");
        ledger.ReferenceOf(snack.Single()).ShouldBe("2026-11");
    }

    [Fact]
    public void Only_card_purchases_move()
    {
        var ledger = new Ledger();
        ledger.Buy(D(10, 1), 5000);
        var refund = Refund.Create(UserId, Card, 1000, D(10, 2), null, PaymentMethod.Credit, "Estorno", null, ledger.Statements)
            .Value.Refund;

        var result = ledger.Move([refund], StatementShift.Next);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Message.ShouldBe("Estorno segue a data dele. Para mudá-lo de fatura, mude a data do estorno.");
    }

    // A compra presa não volta num recálculo, mesmo quando a previsão mudaria (regra 1).
    [Fact]
    public void A_pinned_purchase_survives_a_reconciliation_and_follows_its_statement_due_date()
    {
        var ledger = new Ledger();
        var snack = ledger.Buy(D(9, 25), 3000);
        ledger.Move(snack, StatementShift.Next);

        ledger.Get("2026-11").EditDates(D(10, 26), D(11, 4));
        var result = StatementReconciliation.Reconcile(Card, ledger.Statements, ledger.Transactions);

        result.Moved.ShouldBeEmpty();
        ledger.ReferenceOf(snack.Single()).ShouldBe("2026-11");
        snack.Single().SettlementDate.ShouldBe(D(11, 4));
    }

    [Fact]
    public void Parts_of_different_purchases_are_refused()
    {
        var ledger = new Ledger();
        var a = ledger.Buy(D(9, 20), 1000);
        var b = ledger.Buy(D(9, 21), 1000);

        Should.Throw<ArgumentException>(() => ledger.Move([.. a, .. b], StatementShift.Next));
    }

    // Checkpoint A: parcela acrescentada a uma compra movida continua depois da última, e presa, para as
    // parcelas seguirem em faturas consecutivas.
    [Fact]
    public void An_installment_added_to_a_moved_purchase_goes_after_the_last_one()
    {
        var ledger = new Ledger();
        var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 10000, 3,
            D(9, 20), null, "Tênis", ledger.Statements).Value;
        ledger.Statements.AddRange(created.OpenedStatements);
        ledger.Transactions.AddRange(created.Installments);
        ledger.Move(created.Installments, StatementShift.Next);

        var edit = CardPurchase.Edit(created.Purchase!, Card, created.Installments, ledger.Statements,
            10000, 4, null, "Tênis", D(9, 20)).Value;
        ledger.Statements.AddRange(edit.OpenedStatements);
        ledger.Transactions.AddRange(edit.Added);

        var installments = created.Installments.Concat(edit.Added).ToList();
        installments.Select(ledger.ReferenceOf).ShouldBe(["2026-11", "2026-12", "2027-01", "2027-02"]);
        installments.ShouldAllBe(t => t.StatementPinned);
        installments.Sum(t => t.AmountCents).ShouldBe(10000);
        edit.Added.Single().SettlementDate.ShouldBe(D(2, 5, 2027));
    }

    // Checkpoint A, decisão: a compra foi movida por causa da data antiga; com data nova, a previsão volta a valer.
    [Fact]
    public void Changing_the_date_of_a_moved_purchase_releases_it()
    {
        var ledger = new Ledger();
        var snack = ledger.Buy(D(9, 25), 3000).Single();
        ledger.Move([snack], StatementShift.Next);

        var opened = CardPurchase.EditTransaction(snack, Card, ledger.Statements, Card, TransactionType.Expense, 3000,
            D(9, 20), null, PaymentMethod.Credit, "Lanche").Value;
        ledger.Statements.AddRange(opened);

        snack.StatementPinned.ShouldBeFalse();
        ledger.ReferenceOf(snack).ShouldBe("2026-10");
        snack.SettlementDate.ShouldBe(D(10, 5));
    }

    [Fact]
    public void Changing_the_date_of_a_moved_installment_purchase_releases_all_installments()
    {
        var ledger = new Ledger();
        var created = CardPurchase.Create(UserId, Card, TransactionType.Expense, PaymentMethod.Credit, 10000, 3,
            D(9, 20), null, "Tênis", ledger.Statements).Value;
        ledger.Statements.AddRange(created.OpenedStatements);
        ledger.Transactions.AddRange(created.Installments);
        ledger.Move(created.Installments, StatementShift.Next);

        var edit = CardPurchase.Edit(created.Purchase!, Card, created.Installments, ledger.Statements,
            10000, 3, null, "Tênis", D(9, 21)).Value;
        ledger.Statements.AddRange(edit.OpenedStatements);

        created.Installments.ShouldAllBe(t => !t.StatementPinned);
        created.Installments.Select(ledger.ReferenceOf).ShouldBe(["2026-10", "2026-11", "2026-12"]);
    }
}
