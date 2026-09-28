using CsCheck;
using Prisma.Domain.Accounts;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Transactions;

// docs/fase-2.md, 2.9 (etapa 2.20): o recálculo único decide a fatura de cada compra no cartão,
// respeitando fatura paga e datas editadas. Cartão "fecha 26, vence 5", como o do dono estava
// cadastrado: a fatura 2026-11 fecha em 26/10 e vence em 05/11.
public sealed class StatementReconciliationTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Account Card =
        Account.Create(UserId, "Itaú Visa", AccountType.CreditCard, 0, 26, 5, null).Value;

    private static readonly Account Checking =
        Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;

    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    // Estado gravado de um cartão: faturas e transações, como o banco devolveria.
    private sealed class Ledger(Account card)
    {
        public List<Statement> Statements { get; } = [];
        public List<Transaction> Transactions { get; } = [];

        public IReadOnlyList<Transaction> Buy(DateOnly date, long total, int count = 1)
        {
            var created = CardPurchase.Create(UserId, card, TransactionType.Expense, PaymentMethod.Credit, total, count,
                date, null, "Compra", Statements).Value;
            Statements.AddRange(created.OpenedStatements);
            Transactions.AddRange(created.Installments);
            return created.Installments;
        }

        public Transaction Refund(DateOnly date, long amount)
        {
            var created = Prisma.Domain.Transactions.Refund.Create(
                UserId, card, amount, date, null, PaymentMethod.Credit, "Estorno", null, Statements).Value;
            Statements.AddRange(created.OpenedStatements);
            Transactions.Add(created.Refund);
            return created.Refund;
        }

        public Statement Get(string reference) => Statements.Single(s => s.Reference == reference);

        public Statement Of(Transaction transaction) => Statements.Single(s => s.Id == transaction.StatementId);

        public StatementReconciliation Reconcile()
        {
            var result = StatementReconciliation.Reconcile(card, Statements, Transactions);
            Statements.AddRange(result.Opened);
            return result;
        }
    }

    [Fact]
    public void Without_edited_dates_nothing_moves_and_nothing_opens()
    {
        var ledger = new Ledger(Card);
        ledger.Buy(D(9, 25), 3000);
        ledger.Buy(D(9, 26), 2550);
        ledger.Buy(D(10, 3), 10000, count: 3);
        ledger.Refund(D(10, 10), 1000);

        var result = ledger.Reconcile();

        result.Moved.ShouldBeEmpty();
        result.Opened.ShouldBeEmpty();
    }

    // Caso 5 da tabela: fechamento adiado traz a compra da fatura seguinte.
    [Fact]
    public void A_later_closing_brings_the_purchase_back_from_the_next_statement()
    {
        var ledger = new Ledger(Card);
        var purchase = ledger.Buy(D(10, 27), 4590).Single();
        ledger.Buy(D(10, 20), 1000);
        ledger.Of(purchase).Reference.ShouldBe("2026-12");

        ledger.Get("2026-11").EditDates(D(10, 27), D(11, 5));
        var result = ledger.Reconcile();

        result.Moved.ShouldBe([purchase]);
        ledger.Of(purchase).Reference.ShouldBe("2026-11");
        purchase.SettlementDate.ShouldBe(D(11, 5));
        purchase.PurchaseDate.ShouldBe(D(10, 27));
    }

    // Caso 6 da tabela: fechamento antecipado leva a compra para a fatura seguinte, abrindo-a.
    [Fact]
    public void An_earlier_closing_sends_the_purchase_to_the_next_statement_opening_it()
    {
        var ledger = new Ledger(Card);
        var purchase = ledger.Buy(D(10, 25), 4590).Single();

        ledger.Get("2026-11").EditDates(D(10, 24), D(11, 5));
        var result = ledger.Reconcile();

        result.Opened.Select(s => s.Reference).ShouldBe(["2026-12"]);
        ledger.Of(purchase).Reference.ShouldBe("2026-12");
        purchase.SettlementDate.ShouldBe(D(12, 5));
        purchase.PurchaseDate.ShouldBe(D(10, 25));
    }

    [Fact]
    public void An_installment_purchase_moves_whole_keeping_consecutive_statements_and_the_sum()
    {
        var ledger = new Ledger(Card);
        var installments = ledger.Buy(D(10, 25), 10000, count: 3);
        installments.Select(t => ledger.Of(t).Reference).ShouldBe(["2026-11", "2026-12", "2027-01"]);

        ledger.Get("2026-11").EditDates(D(10, 24), D(11, 5));
        ledger.Reconcile();

        installments.Select(t => ledger.Of(t).Reference).ShouldBe(["2026-12", "2027-01", "2027-02"]);
        installments.Select(t => t.AmountCents).ShouldBe([3334L, 3333L, 3333L]);
        installments.ShouldAllBe(t => t.PurchaseDate == D(10, 25));
    }

    [Fact]
    public void A_purchase_with_an_installment_in_a_paid_statement_does_not_move()
    {
        var ledger = new Ledger(Card);
        var installments = ledger.Buy(D(10, 25), 10000, count: 3);
        ledger.Get("2026-11").EditDates(D(10, 24), D(11, 5));
        ledger.Get("2026-12").MarkAsPaid();

        var result = ledger.Reconcile();

        result.Moved.ShouldBeEmpty();
        installments.Select(t => ledger.Of(t).Reference).ShouldBe(["2026-11", "2026-12", "2027-01"]);
    }

    // A fatura anterior (não paga) tem o fechamento adiado até alcançar a compra, mas a compra está
    // numa fatura paga: não sai dela, senão a fatura paga mudaria de valor.
    [Fact]
    public void A_purchase_in_a_paid_statement_stays_even_when_the_previous_closing_reaches_it()
    {
        var ledger = new Ledger(Card);
        ledger.Buy(D(9, 20), 1000);
        var purchase = ledger.Buy(D(10, 25), 4590).Single();
        ledger.Get("2026-11").MarkAsPaid();

        ledger.Get("2026-10").EditDates(D(10, 25), D(10, 25));
        var result = ledger.Reconcile();

        result.Moved.ShouldBeEmpty();
        ledger.Of(purchase).Reference.ShouldBe("2026-11");
    }

    // Destino pago: a compra fica onde está, e nenhuma fatura é aberta à toa.
    [Fact]
    public void A_purchase_whose_new_statement_is_paid_stays_and_opens_nothing()
    {
        var ledger = new Ledger(Card);
        var purchase = ledger.Buy(D(10, 25), 4590).Single();
        var inDecember = ledger.Buy(D(11, 10), 1000).Single();
        ledger.Get("2026-12").MarkAsPaid();
        ledger.Get("2026-11").EditDates(D(10, 24), D(11, 5));

        var result = ledger.Reconcile();

        result.Moved.ShouldBeEmpty();
        result.Opened.ShouldBeEmpty();
        ledger.Of(purchase).Reference.ShouldBe("2026-11");
        ledger.Of(inDecember).Reference.ShouldBe("2026-12");
    }

    [Fact]
    public void A_refund_on_the_card_follows_its_date()
    {
        var ledger = new Ledger(Card);
        ledger.Buy(D(10, 20), 5000);
        var refund = ledger.Refund(D(10, 27), 1000);
        ledger.Of(refund).Reference.ShouldBe("2026-12");

        ledger.Get("2026-11").EditDates(D(10, 27), D(11, 5));
        ledger.Reconcile();

        ledger.Of(refund).Reference.ShouldBe("2026-11");
        refund.SettlementDate.ShouldBe(D(11, 5));
    }

    [Fact]
    public void A_statement_payment_never_moves()
    {
        var ledger = new Ledger(Card);
        ledger.Buy(D(9, 20), 5000);
        var october = ledger.Get("2026-10");
        // Só a ponta de entrada é do cartão; a de saída fica na conta corrente.
        var legs = Transfer.PayStatement(UserId, october, Card, Checking, 5000, D(10, 1), PaymentMethod.Pix, null).Value;
        ledger.Transactions.Add(legs.In);
        var (paymentStatement, paymentSettlement) = (legs.In.StatementId, legs.In.SettlementDate);
        ledger.Buy(D(10, 25), 1000);

        ledger.Get("2026-11").EditDates(D(10, 24), D(11, 5));
        ledger.Reconcile();

        legs.In.StatementId.ShouldBe(paymentStatement);
        legs.In.SettlementDate.ShouldBe(paymentSettlement);
    }

    [Fact]
    public void Transactions_of_other_accounts_are_refused()
    {
        var ledger = new Ledger(Card);
        var other = Account.Create(UserId, "Outro", AccountType.CreditCard, 0, 10, 20, null).Value;
        var foreign = new Ledger(other);
        foreign.Buy(D(10, 1), 1000);

        Should.Throw<ArgumentException>(() =>
            StatementReconciliation.Reconcile(Card, ledger.Statements.Concat(foreign.Statements).ToList(), foreign.Transactions));
    }

    // ---------- Propriedades ----------

    private sealed record Purchase(int DayOffset, long Total, int Count);

    private static readonly Gen<int> Day = Gen.Int[1, 31];

    private static readonly Gen<Purchase[]> Purchases =
        Gen.Select(Gen.Int[0, 420], Gen.Long[1, 1_000_000], Gen.Int[1, 12])
            .Select((offset, total, count) => new Purchase(offset, Math.Max(total, count), count))
            .Array[1, 25];

    private static readonly DateOnly Start = D(1, 1);

    private static Ledger Build(Account card, Purchase[] purchases)
    {
        var ledger = new Ledger(card);
        foreach (var p in purchases.OrderBy(p => p.DayOffset))
            ledger.Buy(Start.AddDays(p.DayOffset), p.Total, p.Count);
        return ledger;
    }

    private static int MonthIndex(string reference) =>
        int.Parse(reference[..4]) * 12 + int.Parse(reference[5..]);

    // O que as parcelas de uma compra precisam manter: faturas consecutivas e a mesma data.
    private static void ShouldBeConsistent(Ledger ledger)
    {
        foreach (var group in ledger.Transactions.Where(t => t.Type == TransactionType.Expense)
                     .GroupBy(t => t.InstallmentPurchaseId ?? t.Id))
        {
            var months = group.OrderBy(t => t.InstallmentNumber ?? 1).Select(t => MonthIndex(ledger.Of(t).Reference)).ToList();
            for (var i = 1; i < months.Count; i++)
                months[i].ShouldBe(months[i - 1] + 1);
            group.Select(t => t.PurchaseDate).Distinct().Count().ShouldBe(1);
        }
    }

    // Sem edição de datas, o recálculo concorda com o que a criação decidiu: nada muda.
    [Fact]
    public void Property_without_edits_the_reconciliation_agrees_with_creation() =>
        Gen.Select(Day, Day, Purchases).Sample((closing, due, purchases) =>
        {
            var card = Account.Create(UserId, "Cartão", AccountType.CreditCard, 0, closing, due, null).Value;
            var ledger = Build(card, purchases);

            var result = StatementReconciliation.Reconcile(card, ledger.Statements, ledger.Transactions);

            result.Moved.ShouldBeEmpty();
            result.Opened.ShouldBeEmpty();
        }, iter: 300);

    // Com fechamentos editados (sem atravessar as vizinhas), cada compra vai para a fatura certa:
    // a primeira que fecha na data dela ou depois, e a anterior fecha antes. A soma não muda.
    [Fact]
    public void Property_after_edited_closings_every_purchase_lands_in_the_right_statement() =>
        Gen.Select(Day, Day, Purchases, Gen.Int[-3, 3].Array[1, 6]).Sample((closing, due, purchases, shifts) =>
        {
            var card = Account.Create(UserId, "Cartão", AccountType.CreditCard, 0, closing, due, null).Value;
            var ledger = Build(card, purchases);
            var totalBefore = ledger.Transactions.Sum(t => t.AmountCents);

            var ordered = ledger.Statements.OrderBy(s => s.Reference).ToList();
            for (var i = 0; i < shifts.Length && i < ordered.Count; i++)
            {
                var statement = ordered[i];
                var previous = i > 0 ? ordered[i - 1].ClosingDate : DateOnly.MinValue;
                var next = i + 1 < ordered.Count ? ordered[i + 1].ClosingDate : DateOnly.MaxValue;
                var newClosing = statement.ClosingDate.AddDays(shifts[i]);
                if (newClosing <= previous || newClosing >= next)
                    continue;
                statement.EditDates(newClosing, statement.DueDate < newClosing ? newClosing : statement.DueDate);
            }

            ledger.Reconcile();

            ledger.Transactions.Sum(t => t.AmountCents).ShouldBe(totalBefore);
            ShouldBeConsistent(ledger);
            var byReference = ledger.Statements.OrderBy(s => s.Reference).ToList();
            foreach (var first in ledger.Transactions.Where(t => (t.InstallmentNumber ?? 1) == 1))
            {
                var statement = ledger.Of(first);
                statement.ClosingDate.ShouldBeGreaterThanOrEqualTo(first.PurchaseDate);
                var before = byReference.LastOrDefault(s => string.CompareOrdinal(s.Reference, statement.Reference) < 0);
                if (before is not null && MonthIndex(before.Reference) == MonthIndex(statement.Reference) - 1)
                    before.ClosingDate.ShouldBeLessThan(first.PurchaseDate);
            }
            ledger.Transactions.ShouldAllBe(t => t.SettlementDate == ledger.Of(t).DueDate);
        }, iter: 300);

    // Fatura paga é intocável: o que está nela fica, e nada entra nela.
    [Fact]
    public void Property_paid_statements_neither_lose_nor_gain_transactions() =>
        Gen.Select(Day, Day, Purchases, Gen.Int[-3, 3], Gen.Int[0, 100]).Sample((closing, due, purchases, shift, paidCut) =>
        {
            var card = Account.Create(UserId, "Cartão", AccountType.CreditCard, 0, closing, due, null).Value;
            var ledger = Build(card, purchases);
            var ordered = ledger.Statements.OrderBy(s => s.Reference).ToList();
            var paidCount = ordered.Count * paidCut / 100;
            foreach (var statement in ordered.Take(paidCount))
                statement.MarkAsPaid();
            var paidIds = ordered.Take(paidCount).Select(s => s.Id).ToHashSet();
            var inPaidBefore = ledger.Transactions.Where(t => paidIds.Contains(t.StatementId!.Value)).ToHashSet();

            if (paidCount < ordered.Count)
            {
                var first = ordered[paidCount];
                var previous = paidCount > 0 ? ordered[paidCount - 1].ClosingDate : DateOnly.MinValue;
                var next = paidCount + 1 < ordered.Count ? ordered[paidCount + 1].ClosingDate : DateOnly.MaxValue;
                var newClosing = first.ClosingDate.AddDays(shift);
                if (newClosing > previous && newClosing < next)
                    first.EditDates(newClosing, first.DueDate < newClosing ? newClosing : first.DueDate);
            }

            ledger.Reconcile();

            var inPaidAfter = ledger.Transactions.Where(t => paidIds.Contains(t.StatementId!.Value)).ToHashSet();
            inPaidAfter.SetEquals(inPaidBefore).ShouldBeTrue();
            ShouldBeConsistent(ledger);
        }, iter: 300);
}
