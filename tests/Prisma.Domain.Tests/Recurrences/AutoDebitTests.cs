using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Recurrences;

// docs/fase-2.md, 2.15, regras do débito automático e as tabelas "Séries", "Geração e conferência" e
// "Previsão" (o que não depende do banco). Conta corrente Itaú. Os valores esperados saem da
// especificação, com as datas conferidas no calendário.
public sealed class AutoDebitTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    private static Account Itau() => Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
    private static Account Visa() => Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, null).Value;
    private static Account Wallet() => Account.Create(UserId, "Carteira", AccountType.Cash, 0, null, null, null).Value;
    private static readonly Category Utilities = Category.Create(UserId, "Moradia", TransactionType.Expense, null, null, null).Value;
    private static readonly Category Salary = Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    private static readonly AutoDebitTerms Varies = new(AmountVaries: true);
    private static readonly AutoDebitTerms Fixed = new(AmountVaries: false);

    private sealed class Series
    {
        public required Account Account { get; init; }
        public required Recurrence Recurrence { get; init; }
        public required Transaction First { get; init; }
        public List<Transaction> Created { get; } = [];

        public IReadOnlyList<Transaction> Generate(DateOnly today)
        {
            var result = Recurrence.Generate(today, Account, Utilities, []);
            Created.AddRange(result.Created);
            return result.Created;
        }
    }

    private static Result<Recurrence> Start(
        Account account, long cents, DateOnly date, AutoDebitTerms terms, string description = "Sabesp",
        TransactionType type = TransactionType.Expense, RecurrenceFrequency frequency = RecurrenceFrequency.Monthly) =>
        Recurrence.StartFrom(
            Transaction.CreateSimple(UserId, account, type, cents, date, type == TransactionType.Expense ? Utilities : Salary,
                PaymentMethod.Debit, description).Value,
            account, frequency, null, terms);

    private static Series Create(long cents, DateOnly date, AutoDebitTerms terms, string description = "Sabesp")
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, cents, date, Utilities, PaymentMethod.Pix, description).Value;
        var recurrence = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null, terms).Value;
        return new Series { Account = itau, Recurrence = recurrence, First = first };
    }

    // Sabesp: vence todo dia 15, R$ 95,00 médio, partida 15/09/2026.
    private static Series Sabesp() => Create(9500, D(15, 9), Varies);

    private static void ShouldFailWith<T>(Result<T> result, string message)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldBe(message);
    }

    // --- Criar (regras 1, 2, 4 e 5) ---

    [Fact]
    public void Auto_debit_is_a_series_of_its_own_kind_paid_by_debit()
    {
        var series = Sabesp();
        var r = series.Recurrence;

        (r.Kind, r.AmountVaries, r.Method, r.AmountCents).ShouldBe((RecurrenceKind.AutoDebit, true, PaymentMethod.Debit, 9500L));
        (r.StartDate, r.GeneratedThrough).ShouldBe((D(15, 9), D(15, 9)));
        series.First.OccurrenceDate.ShouldBe(D(15, 9));
    }

    // O primeiro lançamento é o que a pessoa digitou: não precisa de conferência (regra 6).
    [Fact]
    public void The_first_transaction_is_not_estimated() =>
        Sabesp().First.AmountEstimated.ShouldBeFalse();

    [Fact]
    public void A_regular_series_stays_regular()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 100000, D(10, 9), Utilities, PaymentMethod.Pix, "Aluguel").Value;
        var r = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null).Value;

        (r.Kind, r.AmountVaries, r.Method).ShouldBe((RecurrenceKind.Regular, false, PaymentMethod.Pix));
    }

    [Fact]
    public void Auto_debit_only_leaves_a_checking_account()
    {
        var message = "Débito automático só sai de conta corrente.";
        ShouldFailWith(Start(Wallet(), 9500, D(15, 9), Varies), message);

        var visa = Visa();
        var purchase = CardPurchase.Create(UserId, visa, TransactionType.Expense, PaymentMethod.Credit, 9500, 1, D(15, 9), Utilities, "Sabesp", []).Value;
        ShouldFailWith(Recurrence.StartFrom(purchase.Installments[0], visa, RecurrenceFrequency.Monthly, null, Varies), message);
    }

    [Fact]
    public void Auto_debit_is_always_an_expense() =>
        ShouldFailWith(Start(Itau(), 500000, D(5, 9), Fixed, "Salário", TransactionType.Income), "Débito automático é sempre despesa.");

    [Fact]
    public void Auto_debit_is_monthly() =>
        ShouldFailWith(Start(Itau(), 9500, D(15, 9), Varies, frequency: RecurrenceFrequency.Weekly), "Débito automático é todo mês.");

    // Copel de 20/09/2026 (domingo), debitada na segunda 21/09: a série parte do vencimento (regra 4).
    [Fact]
    public void The_first_due_date_can_be_before_the_transaction_date()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 18000, D(21, 9), Utilities, PaymentMethod.Debit, "Copel").Value;
        var r = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null, new AutoDebitTerms(true, DueDate: D(20, 9))).Value;

        (r.StartDate, r.GeneratedThrough).ShouldBe((D(20, 9), D(20, 9)));
        first.PurchaseDate.ShouldBe(D(21, 9));
        first.OccurrenceDate.ShouldBe(D(20, 9));
        r.NextTransactionDate.ShouldBe(D(20, 10)); // terça
    }

    [Theory]
    [InlineData(14)] // 7 dias antes: o limite
    [InlineData(21)] // a própria data
    public void The_first_due_date_is_up_to_seven_days_before(int day)
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 18000, D(21, 9), Utilities, PaymentMethod.Debit, "Copel").Value;
        Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null, new AutoDebitTerms(true, D(day, 9))).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, 9)]   // o exemplo da especificação
    [InlineData(13, 9)]  // 8 dias antes
    [InlineData(22, 9)]  // depois do lançamento
    public void A_first_due_date_out_of_the_window_is_refused(int day, int month)
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 18000, D(21, 9), Utilities, PaymentMethod.Debit, "Copel").Value;
        ShouldFailWith(
            Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null, new AutoDebitTerms(true, D(day, month))),
            "O vencimento fica até 7 dias antes da data do lançamento.");
        first.RecurrenceId.ShouldBeNull();
    }

    // Hoje 25/09/2026, lançamento com data 10/09: parte de 10/09; o próximo débito é 13/10 (10/10 é
    // sábado; 12/10, feriado).
    [Fact]
    public void A_past_transaction_starts_the_series_on_its_own_day()
    {
        var series = Create(9500, D(10, 9), Varies, "Escola");

        series.Recurrence.StartDate.ShouldBe(D(10, 9));
        series.Recurrence.NextTransactionDate.ShouldBe(D(13, 10));
        series.Generate(D(25, 9)).ShouldBeEmpty();
    }

    // --- Data do débito (regra 3) e a tabela "Séries" ---

    [Fact]
    public void Sabesp_is_debited_on_the_next_business_day()
    {
        var series = Sabesp();

        series.Generate(D(15, 12));

        series.Created.Select(t => (t.OccurrenceDate, t.PurchaseDate, t.SettlementDate)).ShouldBe([
            (D(15, 10), D(15, 10), D(15, 10)), // quinta
            (D(15, 11), D(16, 11), D(16, 11)), // domingo
            (D(15, 12), D(15, 12), D(15, 12)), // terça
        ]);
    }

    [Fact]
    public void Copel_skips_the_holiday_and_the_sunday()
    {
        var series = Create(18000, D(20, 10), Varies, "Copel");

        series.Generate(D(21, 12));

        series.Created.Select(t => t.PurchaseDate).ShouldBe([D(23, 11), D(21, 12)]);
    }

    [Fact]
    public void Internet_due_on_the_31st_of_october_is_debited_in_november()
    {
        var series = Create(12000, D(31, 8), Fixed, "Internet");

        series.Generate(D(31, 12));

        series.Created.Select(t => t.PurchaseDate).ShouldBe([D(30, 9), D(3, 11), D(30, 11), D(31, 12)]);
    }

    // --- Geração (tabela "Geração e conferência") ---

    [Fact]
    public void Nothing_on_the_sunday_due_date()
    {
        var series = Sabesp();
        series.Generate(D(15, 10));

        series.Generate(D(15, 11)).ShouldBeEmpty();
        series.Recurrence.GeneratedThrough.ShouldBe(D(15, 10));
    }

    [Fact]
    public void On_the_business_day_it_comes_with_the_estimate_to_check()
    {
        var series = Sabesp();
        series.Generate(D(15, 10));

        var created = series.Generate(D(16, 11)).ShouldHaveSingleItem();

        (created.AmountCents, created.PurchaseDate, created.OccurrenceDate, created.AmountEstimated, created.Method)
            .ShouldBe((9500L, D(16, 11), (DateOnly?)D(15, 11), true, PaymentMethod.Debit));
        created.RecurrenceId.ShouldBe(series.Recurrence.Id);
        series.Recurrence.GeneratedThrough.ShouldBe(D(15, 11));
    }

    [Fact]
    public void Generating_twice_creates_one_transaction()
    {
        var series = Sabesp();
        series.Generate(D(16, 11));

        series.Generate(D(16, 11)).ShouldBeEmpty();
        series.Created.Count.ShouldBe(2);
    }

    // API fora do ar de 14/11 a 17/11: em 18/11 a de 15/11 sai uma vez, com a data do débito, não a de hoje.
    [Fact]
    public void Catching_up_keeps_the_debit_date()
    {
        var series = Sabesp();
        series.Generate(D(13, 11));

        series.Generate(D(18, 11)).ShouldHaveSingleItem().PurchaseDate.ShouldBe(D(16, 11));
    }

    [Fact]
    public void A_fixed_amount_comes_without_the_mark()
    {
        var series = Create(12000, D(31, 8), Fixed, "Internet");

        series.Generate(D(30, 9)).ShouldHaveSingleItem().AmountEstimated.ShouldBeFalse();
    }

    [Fact]
    public void An_unchecked_month_does_not_stop_the_next()
    {
        var series = Sabesp();

        series.Generate(D(15, 12));

        series.Created.Count(t => t.AmountEstimated).ShouldBe(3);
    }

    [Fact]
    public void An_inactive_account_skips_without_marking()
    {
        var series = Sabesp();
        series.Account.Update("Itaú", 0, null, null, null, isActive: false);

        series.Generate(D(16, 11)).ShouldBeEmpty();
        series.Recurrence.GeneratedThrough.ShouldBe(D(15, 11));
    }

    // --- Conferir (regra 6) ---

    private static Transaction Estimated()
    {
        var series = Sabesp();
        series.Generate(D(15, 10));
        return series.Created[0];
    }

    [Fact]
    public void Confirming_with_the_real_amount_saves_it_and_clears_the_mark()
    {
        var t = Estimated();

        t.ConfirmAmount(10237).IsSuccess.ShouldBeTrue();

        (t.AmountCents, t.AmountEstimated).ShouldBe((10237L, false));
    }

    [Fact]
    public void Confirming_without_an_amount_keeps_the_estimate()
    {
        var t = Estimated();

        t.ConfirmAmount(null).IsSuccess.ShouldBeTrue();

        (t.AmountCents, t.AmountEstimated).ShouldBe((9500L, false));
    }

    [Fact]
    public void Confirming_does_not_change_the_series_estimate()
    {
        var series = Sabesp();
        series.Generate(D(15, 10));

        series.Created[0].ConfirmAmount(10237);
        series.Generate(D(16, 11));

        series.Recurrence.AmountCents.ShouldBe(9500);
        series.Created[1].AmountCents.ShouldBe(9500);
    }

    [Fact]
    public void Confirming_twice_is_refused()
    {
        var t = Estimated();
        t.ConfirmAmount(null);

        ShouldFailWith(t.ConfirmAmount(10237), "Este lançamento já foi conferido.");
        t.AmountCents.ShouldBe(9500);
    }

    [Fact]
    public void A_transaction_that_was_never_estimated_is_not_confirmed() =>
        ShouldFailWith(Sabesp().First.ConfirmAmount(null), "Este lançamento já foi conferido.");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Confirming_needs_a_positive_amount(long cents)
    {
        var t = Estimated();

        ShouldFailWith(t.ConfirmAmount(cents), "O valor deve ser maior que zero.");
        t.AmountEstimated.ShouldBeTrue();
    }

    [Fact]
    public void Editing_the_amount_confirms()
    {
        var t = Estimated();
        var itau = Itau();

        t.UpdateSimple(itau, TransactionType.Expense, 10237, t.PurchaseDate, Utilities, PaymentMethod.Debit, "Sabesp");

        (t.AmountCents, t.AmountEstimated).ShouldBe((10237L, false));
    }

    [Fact]
    public void Editing_only_the_category_or_description_keeps_the_mark()
    {
        var t = Estimated();

        t.UpdateSimple(Itau(), TransactionType.Expense, 9500, t.PurchaseDate, null, PaymentMethod.Debit, "Sabesp casa");

        t.AmountEstimated.ShouldBeTrue();
    }

    [Fact]
    public void Restoring_brings_it_back_still_to_check()
    {
        // A exclusão é gravada pela infraestrutura (DeletedAt); restaurar não mexe na marca.
        var t = Estimated();

        t.Restore(categoryStillExists: false);

        t.AmountEstimated.ShouldBeTrue();
    }

    // --- Mudar o tipo da série (regra 10) ---

    private static Result<Recurrence> Edit(Series s, AutoDebitTerms? terms, Account? account = null, RecurrenceFrequency frequency = RecurrenceFrequency.Monthly,
        PaymentMethod method = PaymentMethod.Pix, DateOnly? nextDate = null) =>
        s.Recurrence.Edit(account ?? s.Account, s.Recurrence.AmountCents, Utilities, s.Recurrence.Description, method, frequency, nextDate, null, terms);

    private static Series Rent()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 9500, D(15, 9), Utilities, PaymentMethod.Pix, "Sabesp").Value;
        return new Series { Account = itau, Recurrence = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null).Value, First = first };
    }

    [Fact]
    public void A_regular_series_becomes_auto_debit_from_the_next_on()
    {
        var series = Rent();
        series.Generate(D(15, 10));

        Edit(series, Varies).IsSuccess.ShouldBeTrue();
        series.Generate(D(16, 11));

        (series.Recurrence.Kind, series.Recurrence.Method).ShouldBe((RecurrenceKind.AutoDebit, PaymentMethod.Debit));
        series.Created.Select(t => (t.PurchaseDate, t.AmountEstimated, t.Method)).ShouldBe([
            (D(15, 10), false, PaymentMethod.Pix),
            (D(16, 11), true, PaymentMethod.Debit),
        ]);
    }

    [Fact]
    public void Stopping_the_auto_debit_goes_back_to_the_schedule_date()
    {
        var series = Sabesp();
        series.Generate(D(15, 10));

        Edit(series, terms: null, method: PaymentMethod.Pix).IsSuccess.ShouldBeTrue();
        series.Generate(D(15, 11));

        (series.Recurrence.Kind, series.Recurrence.AmountVaries).ShouldBe((RecurrenceKind.Regular, false));
        series.Created.Select(t => (t.PurchaseDate, t.AmountEstimated)).ShouldBe([(D(15, 10), true), (D(15, 11), false)]);
    }

    [Fact]
    public void Editing_to_a_fixed_amount_stops_marking()
    {
        var series = Sabesp();

        Edit(series, Fixed).IsSuccess.ShouldBeTrue();
        series.Generate(D(15, 10));

        series.Created.ShouldHaveSingleItem().AmountEstimated.ShouldBeFalse();
    }

    [Fact]
    public void Editing_keeps_the_auto_debit_rules()
    {
        var weekly = Rent();
        ShouldFailWith(Edit(weekly, Varies, frequency: RecurrenceFrequency.Weekly, nextDate: D(22, 9)), "Débito automático é todo mês.");
        weekly.Recurrence.Kind.ShouldBe(RecurrenceKind.Regular);

        var cash = Wallet();
        var first = Transaction.CreateSimple(UserId, cash, TransactionType.Expense, 9500, D(15, 9), Utilities, PaymentMethod.Cash, "Feira").Value;
        var onCash = new Series { Account = cash, Recurrence = Recurrence.StartFrom(first, cash, RecurrenceFrequency.Monthly, null).Value, First = first };
        ShouldFailWith(Edit(onCash, Varies, method: PaymentMethod.Cash), "Débito automático só sai de conta corrente.");

        ShouldFailWith(Edit(Sabesp(), Varies, account: Wallet(), method: PaymentMethod.Cash), "Débito automático só sai de conta corrente.");
    }

    // --- Previsão (regra 9 e a tabela "Previsão") ---

    // Hoje 20/10/2026, com Sabesp (desde 15/09), Copel (desde 20/10) e Internet (desde 31/08).
    [Fact]
    public void Forecast_counts_each_debit_in_the_month_it_is_debited()
    {
        var sabesp = Sabesp();
        var copel = Create(18000, D(20, 10), Varies, "Copel");
        var internet = Create(12000, D(31, 8), Fixed, "Internet");
        var all = new[] { sabesp, copel, internet };
        foreach (var s in all)
            s.Generate(D(20, 10));

        var projected = RecurrenceProjection.Of(
            all.Select(s => s.Recurrence), all.Select(s => s.Account).ToList(), [], until: D(30, 11));

        projected.Select(p => (p.OccurrenceDate, p.SettlementDate, p.AmountCents)).OrderBy(p => p.SettlementDate).ShouldBe([
            (D(31, 10), D(3, 11), 12000L),
            (D(15, 11), D(16, 11), 9500L),
            (D(20, 11), D(23, 11), 18000L),
            (D(30, 11), D(30, 11), 12000L), // a Internet de novembro: duas no mês
        ]);

        var months = CommittedMonths.Of(D(20, 10), [], null, projected).Months;
        months[0].ProjectedExpenseCents.ShouldBe(51500); // novembro
        months.Where(m => m.Month == D(1, 10)).ShouldBeEmpty(); // outubro não está no horizonte, e nada fica nele
    }

    // No domingo 15/11 a de 15/11 ainda não foi gerada: continua prevista para 16/11, sem buraco.
    [Fact]
    public void Forecast_keeps_the_due_date_not_yet_debited()
    {
        var series = Sabesp();
        series.Generate(D(15, 11));

        RecurrenceProjection.Of([series.Recurrence], [series.Account], [], until: D(16, 11))
            .ShouldHaveSingleItem().SettlementDate.ShouldBe(D(16, 11));
    }
}
