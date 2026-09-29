using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Recurrences;

// docs/fase-2.md, 2.14, regras 1, 2, 5 a 9 e a tabela "Geração" (o que não depende do banco). Visa "fecha
// 26, vence 5"; Itaú conta corrente. O pet é a cobrança de R$ 400,00 todo dia 25 no cartão do pai do dono.
public sealed class RecurrenceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    private static Account Visa() => Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, null).Value;
    private static Account Master() => Account.Create(UserId, "Master", AccountType.CreditCard, 0, 5, 12, null).Value;
    private static Account Itau() => Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
    private static readonly Category Pet = Category.Create(UserId, "Pet", TransactionType.Expense, null, null, null).Value;
    private static readonly Category Salary = Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    private sealed class Series
    {
        public required Account Account { get; init; }
        public required Recurrence Recurrence { get; init; }
        public required Transaction First { get; init; }
        public List<Statement> Statements { get; } = [];
        public List<Transaction> Created { get; } = [];

        public RecurrenceGeneration Generate(DateOnly today, Category? category = null)
        {
            var result = Recurrence.Generate(today, Account, category ?? Pet, Statements);
            Statements.AddRange(result.Opened);
            Created.AddRange(result.Created);
            return result;
        }

        public Statement StatementOf(Transaction t) => Statements.Single(s => s.Id == t.StatementId);
    }

    // O pai lançou a cobrança de 25/09 e diz que ela se repete todo mês.
    private static Series PetOnVisa(DateOnly? end = null)
    {
        var visa = Visa();
        var first = CardPurchase.Create(UserId, visa, TransactionType.Expense, PaymentMethod.Credit, 40000, 1, D(25, 9),
            Pet, "Pet", []).Value;
        var recurrence = Recurrence.StartFrom(first.Installments[0], visa, RecurrenceFrequency.Monthly, end).Value;
        var series = new Series { Account = visa, Recurrence = recurrence, First = first.Installments[0] };
        series.Statements.AddRange(first.OpenedStatements);
        return series;
    }

    private static Series RentOnItau()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 100000, D(10, 9), Pet, PaymentMethod.Pix, "Aluguel").Value;
        return new Series { Account = itau, Recurrence = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null).Value, First = first };
    }

    private static void ShouldFailWith<T>(Result<T> result, string message, ErrorType type = ErrorType.Validation)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(type);
        result.Error.Message.ShouldBe(message);
    }

    // --- Criar (regras 1 e 2) ---

    [Fact]
    public void The_first_charge_becomes_the_first_occurrence()
    {
        var series = PetOnVisa();
        var r = series.Recurrence;

        (r.AccountId, r.Type, r.AmountCents, r.CategoryId, r.Description, r.Method)
            .ShouldBe((series.Account.Id, TransactionType.Expense, 40000L, (Guid?)Pet.Id, "Pet", PaymentMethod.Credit));
        (r.Frequency, r.StartDate, r.EndDate, r.GeneratedThrough).ShouldBe((RecurrenceFrequency.Monthly, D(25, 9), (DateOnly?)null, D(25, 9)));
        r.UserId.ShouldBe(UserId);
        series.First.RecurrenceId.ShouldBe(r.Id);
        series.First.OccurrenceDate.ShouldBe(D(25, 9));
        r.NextOccurrence.ShouldBe(D(25, 10));
    }

    [Fact]
    public void Refunds_transfers_and_installment_purchases_do_not_repeat()
    {
        var visa = Visa();
        var itau = Itau();
        var purchase = CardPurchase.Create(UserId, visa, TransactionType.Expense, PaymentMethod.Credit, 30000, 3, D(25, 9), Pet, "Tênis", []).Value;
        var refund = Refund.Create(UserId, visa, 1000, D(26, 9), null, PaymentMethod.Credit, null, null, purchase.OpenedStatements).Value.Refund;
        var savings = Account.Create(UserId, "Poupança", AccountType.Investment, 0, null, null, null).Value;
        var transfer = Transfer.Create(UserId, itau, savings, 5000, D(25, 9), PaymentMethod.Pix, null).Value;

        ShouldFailWith(Recurrence.StartFrom(purchase.Installments[0], visa, RecurrenceFrequency.Monthly, null), "Compra parcelada não se repete.");
        ShouldFailWith(Recurrence.StartFrom(refund, visa, RecurrenceFrequency.Monthly, null), "Estorno não se repete.");
        ShouldFailWith(Recurrence.StartFrom(transfer.Out, itau, RecurrenceFrequency.Monthly, null), "Transferência entre contas não se repete.");
    }

    [Fact]
    public void A_transaction_joins_one_series_only()
    {
        var series = PetOnVisa();

        ShouldFailWith(Recurrence.StartFrom(series.First, series.Account, RecurrenceFrequency.Weekly, null),
            "Este lançamento já faz parte de uma série.", ErrorType.Conflict);
    }

    [Fact]
    public void The_end_comes_after_the_first_occurrence()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Expense, 100000, D(10, 9), null, PaymentMethod.Pix, "Aluguel").Value;

        ShouldFailWith(Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, D(10, 9)),
            "O término deve ser depois do primeiro lançamento (10/09/2026).");
        first.RecurrenceId.ShouldBeNull();
    }

    // --- Gerar (regras 3 a 5 e a tabela "Geração") ---

    [Fact]
    public void Nothing_is_generated_before_the_day()
    {
        var series = PetOnVisa();

        series.Generate(D(24, 10)).Created.ShouldBeEmpty();

        series.Recurrence.GeneratedThrough.ShouldBe(D(25, 9));
    }

    // 25/10 é domingo: a cobrança não muda de dia, e a fatura sai da regra do cartão.
    [Fact]
    public void The_pet_of_october_25_is_a_card_charge_on_the_november_statement()
    {
        var series = PetOnVisa();

        var result = series.Generate(D(25, 10));

        var charge = result.Created.ShouldHaveSingleItem();
        (charge.AccountId, charge.Type, charge.Method, charge.AmountCents).ShouldBe((series.Account.Id, TransactionType.Expense, PaymentMethod.Credit, 40000L));
        (charge.PurchaseDate, charge.SettlementDate, charge.CategoryId, charge.Description).ShouldBe((D(25, 10), D(5, 11), (Guid?)Pet.Id, "Pet"));
        charge.InstallmentPurchaseId.ShouldBeNull();
        (charge.RecurrenceId, charge.OccurrenceDate).ShouldBe(((Guid?)series.Recurrence.Id, (DateOnly?)D(25, 10)));
        series.StatementOf(charge).Reference.ShouldBe("2026-11");
        result.Opened.ShouldHaveSingleItem().Reference.ShouldBe("2026-11");
        series.Recurrence.GeneratedThrough.ShouldBe(D(25, 10));
        series.Recurrence.NextOccurrence.ShouldBe(D(25, 11));
    }

    [Fact]
    public void Generating_again_the_same_day_creates_nothing()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));

        series.Generate(D(25, 10)).Created.ShouldBeEmpty();
        series.Generate(D(26, 10)).Created.ShouldBeEmpty();

        series.Created.ShouldHaveSingleItem();
    }

    // A geração atrasada alcança de uma vez o que faltou, cada um no seu dia.
    [Fact]
    public void Rent_by_pix_catches_up_every_missed_month()
    {
        var series = RentOnItau();

        var created = series.Generate(D(11, 12)).Created;

        created.Select(t => (t.PurchaseDate, t.SettlementDate)).ShouldBe([(D(10, 10), D(10, 10)), (D(10, 11), D(10, 11)), (D(10, 12), D(10, 12))]);
        created.ShouldAllBe(t => t.Method == PaymentMethod.Pix && t.StatementId == null && t.AmountCents == 100000);
        series.Recurrence.GeneratedThrough.ShouldBe(D(10, 12));
    }

    // Duas cobranças semanais que caem na mesma fatura nova: ela é aberta uma vez só.
    [Fact]
    public void Weekly_charges_share_the_statement_they_open()
    {
        var visa = Visa();
        var first = CardPurchase.Create(UserId, visa, TransactionType.Expense, PaymentMethod.Credit, 5000, 1, D(2, 10), null, "Diarista", []).Value;
        var series = new Series
        {
            Account = visa, First = first.Installments[0],
            Recurrence = Recurrence.StartFrom(first.Installments[0], visa, RecurrenceFrequency.Weekly, null).Value,
        };
        series.Statements.AddRange(first.OpenedStatements);

        var result = series.Recurrence.Generate(D(16, 10), visa, null, series.Statements);

        result.Created.Select(t => t.PurchaseDate).ShouldBe([D(9, 10), D(16, 10)]);
        result.Created.Select(t => t.StatementId).Distinct().ShouldHaveSingleItem();
        result.Opened.ShouldBeEmpty(); // a fatura 2026-11 já existia, aberta pela de 02/10
    }

    [Fact]
    public void Income_repeats_off_the_card()
    {
        var itau = Itau();
        var first = Transaction.CreateSimple(UserId, itau, TransactionType.Income, 820000, D(5, 9), Salary, PaymentMethod.Ted, "Salário").Value;
        var recurrence = Recurrence.StartFrom(first, itau, RecurrenceFrequency.Monthly, null).Value;

        var created = recurrence.Generate(D(5, 10), itau, Salary, []).Created.ShouldHaveSingleItem();

        (created.Type, created.AmountCents, created.PurchaseDate, created.CategoryId).ShouldBe((TransactionType.Income, 820000L, D(5, 10), (Guid?)Salary.Id));
    }

    // Categoria excluída: o lançamento sai sem categoria (regra 8).
    [Fact]
    public void A_deleted_category_generates_without_category()
    {
        var series = RentOnItau();

        var created = series.Recurrence.Generate(D(10, 10), series.Account, null, []).Created.ShouldHaveSingleItem();

        created.CategoryId.ShouldBeNull();
    }

    // --- Editar e encerrar (regras 6 e 7) ---

    [Fact]
    public void A_new_amount_applies_from_the_next_occurrence_on()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));
        var r = series.Recurrence;

        r.Edit(series.Account, 45000, Pet, "Pet", PaymentMethod.Credit, r.Frequency, nextDate: null, r.EndDate).IsSuccess.ShouldBeTrue();
        series.Generate(D(25, 11));

        series.Created.Select(t => t.AmountCents).ShouldBe([40000L, 45000L]);
        series.First.AmountCents.ShouldBe(40000);
    }

    // Todo dia 25 passa a ser todo dia 5, a partir de 05/11: a agenda recomeça na nova data.
    [Fact]
    public void A_new_schedule_starts_on_the_next_date()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));
        var r = series.Recurrence;

        r.Edit(series.Account, r.AmountCents, Pet, r.Description, r.Method, RecurrenceFrequency.Monthly, nextDate: D(5, 11), null)
            .IsSuccess.ShouldBeTrue();

        r.NextOccurrence.ShouldBe(D(5, 11));
        series.Generate(D(5, 12)).Created.Select(t => t.PurchaseDate).ShouldBe([D(5, 11), D(5, 12)]);
    }

    [Fact]
    public void The_next_date_comes_after_the_last_generated_one()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));
        var r = series.Recurrence;

        ShouldFailWith(r.Edit(series.Account, r.AmountCents, Pet, r.Description, r.Method, RecurrenceFrequency.Weekly, nextDate: D(25, 10), null),
            "A próxima data tem de ser depois do último lançamento (25/10/2026).");
        ShouldFailWith(r.Edit(series.Account, r.AmountCents, Pet, r.Description, r.Method, RecurrenceFrequency.Weekly, nextDate: null, null),
            "Informe a próxima data da nova frequência.");
        r.Frequency.ShouldBe(RecurrenceFrequency.Monthly);
    }

    [Fact]
    public void The_end_is_not_before_the_last_generated_one()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));
        var r = series.Recurrence;

        ShouldFailWith(r.Edit(series.Account, r.AmountCents, Pet, r.Description, r.Method, r.Frequency, null, D(24, 10)),
            "O término não pode ser antes do último lançamento (25/10/2026).");
    }

    // Cartão por cartão, conta por conta: a série não muda de natureza.
    [Fact]
    public void The_series_moves_between_cards_but_not_off_the_card()
    {
        var series = PetOnVisa();
        var r = series.Recurrence;
        var master = Master();

        ShouldFailWith(r.Edit(Itau(), r.AmountCents, Pet, r.Description, PaymentMethod.Pix, r.Frequency, null, null),
            "A série continua no mesmo tipo de conta: cartão por cartão, conta por conta.");
        r.Edit(master, r.AmountCents, Pet, r.Description, PaymentMethod.Credit, r.Frequency, null, null).IsSuccess.ShouldBeTrue();

        r.AccountId.ShouldBe(master.Id);
        var charge = r.Generate(D(25, 10), master, Pet, []).Created.ShouldHaveSingleItem();
        (charge.AccountId, charge.SettlementDate).ShouldBe((master.Id, D(12, 11))); // Master fecha 05/11, vence 12/11
    }

    [Fact]
    public void Editing_checks_the_template()
    {
        var series = PetOnVisa();
        var r = series.Recurrence;

        ShouldFailWith(r.Edit(series.Account, 0, Pet, r.Description, r.Method, r.Frequency, null, null), "O valor deve ser maior que zero.");
        ShouldFailWith(r.Edit(series.Account, r.AmountCents, Salary, r.Description, r.Method, r.Frequency, null, null),
            "A categoria deve ser do mesmo tipo do lançamento (receita ou despesa).");
        ShouldFailWith(r.Edit(series.Account, r.AmountCents, Pet, new string('x', 201), r.Method, r.Frequency, null, null),
            "A descrição deve ter no máximo 200 caracteres.");
        r.AmountCents.ShouldBe(40000);
    }

    // Encerrar para no último lançamento gerado: nada mais sai, nem a ocorrência de hoje que a tarefa
    // ainda não tinha gerado.
    [Fact]
    public void Ending_stops_at_the_last_generated_occurrence()
    {
        var series = PetOnVisa();
        series.Generate(D(25, 10));

        series.Recurrence.End();
        var later = series.Generate(D(25, 12));

        later.Created.ShouldBeEmpty();
        series.Recurrence.EndDate.ShouldBe(D(25, 10));
        series.Recurrence.NextOccurrence.ShouldBeNull();
        series.Created.ShouldHaveSingleItem();
    }

    [Fact]
    public void An_end_date_is_inclusive()
    {
        var series = PetOnVisa(end: D(30, 11));

        series.Generate(D(31, 12)).Created.Select(t => t.PurchaseDate).ShouldBe([D(25, 10), D(25, 11)]);
    }

    // --- Conta inativa e fatura paga (regras 8 e 9) ---

    [Fact]
    public void An_inactive_card_skips_its_occurrences()
    {
        var series = PetOnVisa();
        series.Account.Update("Visa", 0, 26, 5, null, isActive: false);

        var paused = series.Generate(D(25, 10));
        series.Account.Update("Visa", 0, 26, 5, null, isActive: true);
        var back = series.Generate(D(25, 11));

        (paused.Created.Count, paused.Pending.Count).ShouldBe((0, 0));
        back.Created.Select(t => t.PurchaseDate).ShouldBe([D(25, 11)]);
    }

    // Só com a geração atrasada: a fatura de novembro fechou em 26/10, foi paga em 27/10, e a tarefa
    // rodou só em 28/10. A cobrança fica pendente, e as seguintes saem normalmente.
    [Fact]
    public void A_charge_that_would_land_on_a_paid_statement_becomes_pending()
    {
        var series = PetOnVisa();
        var november = Statement.Open(UserId, series.Account.Id, new StatementDates("2026-11", D(26, 10), D(5, 11)));
        november.MarkAsPaid();
        series.Statements.Add(november);

        var late = series.Generate(D(28, 10));
        var next = series.Generate(D(25, 11));

        late.Created.ShouldBeEmpty();
        late.Pending.ShouldHaveSingleItem().ShouldBe(new PendingOccurrence(D(25, 10), "2026-11"));
        series.Recurrence.GeneratedThrough.ShouldBe(D(25, 11));
        next.Created.ShouldHaveSingleItem().PurchaseDate.ShouldBe(D(25, 11));
    }
    // --- Resolver a pendência (regra 9): lançar na fatura seguinte, lançar depois de desfazer o pagamento ---

    // O pet de 25/10 ficou pendente: a fatura 2026-11 (fecha 26/10, vence 05/11) foi paga antes da geração.
    private static (Series Series, RecurrencePending Pending) PendingPet()
    {
        var series = PetOnVisa();
        var november = Statement.Open(UserId, series.Account.Id, new StatementDates("2026-11", D(26, 10), D(5, 11)));
        november.MarkAsPaid();
        series.Statements.Add(november);
        var pending = series.Generate(D(28, 10)).Pending.Single();
        return (series, RecurrencePending.For(series.Recurrence, pending));
    }

    [Fact]
    public void A_pending_keeps_the_card_and_the_amount_of_its_day()
    {
        var (series, pending) = PendingPet();

        (pending.RecurrenceId, pending.AccountId, pending.AmountCents, pending.OccurrenceDate, pending.StatementReference)
            .ShouldBe((series.Recurrence.Id, series.Account.Id, 40000L, D(25, 10), "2026-11"));
    }

    [Fact]
    public void Launching_in_the_next_statement_pins_the_charge_to_december()
    {
        var (series, pending) = PendingPet();

        var launched = series.Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.NextStatement).Value;

        var charge = launched.Installments.ShouldHaveSingleItem();
        var december = launched.OpenedStatements.ShouldHaveSingleItem();
        (december.Reference, december.ClosingDate, december.DueDate).ShouldBe(("2026-12", D(26, 11), D(5, 12)));
        (charge.StatementId, charge.SettlementDate, charge.StatementPinned).ShouldBe(((Guid?)december.Id, D(5, 12), true));
        (charge.PurchaseDate, charge.AmountCents, charge.CategoryId, charge.Description).ShouldBe((D(25, 10), 40000L, (Guid?)Pet.Id, "Pet"));
        (charge.RecurrenceId, charge.OccurrenceDate).ShouldBe(((Guid?)series.Recurrence.Id, (DateOnly?)D(25, 10)));
        launched.Purchase.ShouldBeNull();
    }

    // A de 25/11 já abriu a fatura de dezembro: a pendência entra nela, sem abrir outra.
    [Fact]
    public void Launching_in_the_next_statement_reuses_it_when_it_exists()
    {
        var (series, pending) = PendingPet();
        series.Generate(D(25, 11));
        var december = series.Statements.Single(s => s.Reference == "2026-12");

        var launched = series.Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.NextStatement).Value;

        launched.OpenedStatements.ShouldBeEmpty();
        launched.Installments.Single().StatementId.ShouldBe(december.Id);
    }

    [Fact]
    public void The_next_statement_cannot_be_paid_too()
    {
        var (series, pending) = PendingPet();
        var december = Statement.Open(UserId, series.Account.Id, new StatementDates("2026-12", D(26, 11), D(5, 12)));
        december.MarkAsPaid();
        series.Statements.Add(december);

        ShouldFailWith(series.Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.NextStatement),
            "A fatura seguinte também já está paga.");
    }

    [Fact]
    public void Launching_in_its_own_statement_needs_the_payment_undone()
    {
        var (series, pending) = PendingPet();

        ShouldFailWith(series.Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.SameStatement),
            "A fatura desta cobrança continua paga. Desfaça o pagamento ou lance na fatura seguinte.");
    }

    // Pagamento desfeito: a fatura de novembro volta a aceitar compras, e a cobrança entra nela pela regra de
    // sempre, sem ficar presa. A fatura é outra instância com a mesma referência, como viria do banco.
    [Fact]
    public void After_the_payment_is_undone_the_charge_lands_on_november()
    {
        var (series, pending) = PendingPet();
        var november = Statement.Open(UserId, series.Account.Id, new StatementDates("2026-11", D(26, 10), D(5, 11)));
        var statements = series.Statements.Where(s => s.Reference != "2026-11").Append(november).ToList();

        var launched = series.Recurrence.LaunchPending(pending, series.Account, Pet, statements, PendingLaunch.SameStatement).Value;

        var charge = launched.Installments.ShouldHaveSingleItem();
        (charge.StatementId, charge.SettlementDate, charge.StatementPinned).ShouldBe(((Guid?)november.Id, D(5, 11), false));
        (charge.RecurrenceId, charge.OccurrenceDate).ShouldBe(((Guid?)series.Recurrence.Id, (DateOnly?)D(25, 10)));
    }

    // Regra 6: a edição vale do próximo em diante. A cobrança de 25/10 venceu antes dela: sai com R$ 400,00 no
    // Visa, mesmo que a série agora seja R$ 450,00 no Master.
    [Fact]
    public void A_later_edit_does_not_change_the_pending_charge()
    {
        var (series, pending) = PendingPet();
        series.Recurrence.Edit(Master(), 45000, Pet, "Pet", PaymentMethod.Credit, RecurrenceFrequency.Monthly, null, null)
            .IsSuccess.ShouldBeTrue();

        var launched = series.Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.NextStatement).Value;

        var charge = launched.Installments.Single();
        (charge.AccountId, charge.AmountCents).ShouldBe((series.Account.Id, 40000L));
    }

    [Fact]
    public void The_card_given_must_be_the_one_of_the_pending()
    {
        var (series, pending) = PendingPet();

        Should.Throw<ArgumentException>(() =>
            series.Recurrence.LaunchPending(pending, Master(), Pet, series.Statements, PendingLaunch.NextStatement));
        Should.Throw<ArgumentException>(() =>
            PetOnVisa().Recurrence.LaunchPending(pending, series.Account, Pet, series.Statements, PendingLaunch.NextStatement));
    }
}
