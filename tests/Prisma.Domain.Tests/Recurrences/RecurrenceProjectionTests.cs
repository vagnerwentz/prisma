using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;
using Shouldly;

namespace Prisma.Domain.Tests.Recurrences;

// docs/fase-2.md, 2.14, regra 11 e a tabela "Previsão": hoje 20/10/2026, com o pet (R$ 400,00 todo dia 25 no
// Visa "fecha 26, vence 5", desde 25/09) e o aluguel (R$ 1.000,00 todo dia 10 por Pix no Itaú, desde 10/09).
// A previsão é calculada e nunca gravada: as ocorrências depois de GeneratedThrough.
public sealed class RecurrenceProjectionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    private static readonly Category Pet = Category.Create(UserId, "Pet", TransactionType.Expense, null, null, null).Value;
    private static readonly Category Salary = Category.Create(UserId, "Salário", TransactionType.Income, null, null, null).Value;

    private sealed class World
    {
        public Account Visa { get; } = Account.Create(UserId, "Visa", AccountType.CreditCard, 0, 26, 5, null).Value;
        public Account Itau { get; } = Account.Create(UserId, "Itaú", AccountType.Checking, 0, null, null, null).Value;
        public List<Statement> Statements { get; } = [];
        public List<Recurrence> Recurrences { get; } = [];

        public Recurrence PetSeries()
        {
            var first = CardPurchase.Create(UserId, Visa, TransactionType.Expense, PaymentMethod.Credit, 40000, 1, D(25, 9), Pet, "Pet",
                Statements).Value;
            Statements.AddRange(first.OpenedStatements);
            return Add(Recurrence.StartFrom(first.Installments[0], Visa, RecurrenceFrequency.Monthly, null).Value);
        }

        public Recurrence RentSeries()
        {
            var first = Transaction.CreateSimple(UserId, Itau, TransactionType.Expense, 100000, D(10, 9), Pet, PaymentMethod.Pix, "Aluguel").Value;
            return Add(Recurrence.StartFrom(first, Itau, RecurrenceFrequency.Monthly, null).Value);
        }

        public Recurrence Add(Recurrence recurrence)
        {
            Recurrences.Add(recurrence);
            return recurrence;
        }

        // A tarefa já gerou o que venceu até hoje (o aluguel de 10/10, em 20/10).
        public void GenerateThrough(DateOnly today)
        {
            foreach (var r in Recurrences)
            {
                var account = r.AccountId == Visa.Id ? Visa : Itau;
                Statements.AddRange(r.Generate(today, account, Pet, Statements).Opened);
            }
        }

        public IReadOnlyList<ProjectedOccurrence> Project(DateOnly until) =>
            RecurrenceProjection.Of(Recurrences, [Visa, Itau], Statements, until);

        public CommittedMonths Committed(DateOnly today) =>
            CommittedMonths.Of(today, [], null, Project(new DateOnly(today.Year, today.Month, 1).AddMonths(CommittedMonths.Horizon + 1).AddDays(-1)));
    }

    private static World PetAndRentOn20October()
    {
        var world = new World();
        world.PetSeries();
        world.RentSeries();
        world.GenerateThrough(D(20, 10));
        return world;
    }

    [Fact]
    public void November_and_december_each_have_1400_projected()
    {
        var committed = PetAndRentOn20October().Committed(D(20, 10));

        committed.Months[0].Month.ShouldBe(D(1, 11));
        committed.Months[0].ProjectedExpenseCents.ShouldBe(140000); // o pet de 25/10 (vence 05/11) e o aluguel de 10/11
        committed.Months[1].ProjectedExpenseCents.ShouldBe(140000); // o pet de 25/11 (vence 05/12) e o aluguel de 10/12
        committed.Months[0].ExpenseCents.ShouldBe(0); // o que existe continua separado
    }

    [Fact]
    public void The_open_statement_expects_the_pet_until_it_closes()
    {
        var world = PetAndRentOn20October();

        var projected = world.Project(D(26, 10));

        RecurrenceProjection.ForStatement(projected, world.Visa.Id, "2026-11").ShouldBe(40000);
        var pet = projected.Single(p => p.AccountId == world.Visa.Id);
        (pet.OccurrenceDate, pet.SettlementDate, pet.StatementReference).ShouldBe((D(25, 10), D(5, 11), "2026-11"));

        // Com a previsão mais longa, cada fatura fica só com as cobranças que caem nela.
        var longer = world.Project(D(26, 11));
        RecurrenceProjection.ForStatement(longer, world.Visa.Id, "2026-11").ShouldBe(40000);
        RecurrenceProjection.ForStatement(longer, world.Visa.Id, "2026-12").ShouldBe(40000);
    }

    // Nada contado duas vezes: gerado o pet de 25/10, ele sai da previsão (e entra no total da fatura).
    [Fact]
    public void Once_generated_the_pet_leaves_the_projection()
    {
        var world = PetAndRentOn20October();
        world.GenerateThrough(D(25, 10));

        RecurrenceProjection.ForStatement(world.Project(D(26, 10)), world.Visa.Id, "2026-11").ShouldBe(0);
        world.Committed(D(25, 10)).Months[0].ProjectedExpenseCents.ShouldBe(100000); // só o aluguel de 10/11
    }

    // Entre a meia-noite e a próxima execução, a ocorrência de hoje ainda não foi gerada: continua prevista.
    [Fact]
    public void An_occurrence_not_generated_yet_is_still_projected()
    {
        var world = PetAndRentOn20October();

        world.Project(D(25, 10)).Select(p => p.OccurrenceDate).ShouldContain(D(25, 10));
    }

    [Fact]
    public void An_ended_series_projects_nothing()
    {
        var world = PetAndRentOn20October();
        foreach (var r in world.Recurrences)
            r.End();

        world.Project(D(31, 12)).ShouldBeEmpty();
    }

    [Fact]
    public void The_end_date_limits_the_projection()
    {
        var world = new World();
        var first = Transaction.CreateSimple(UserId, world.Itau, TransactionType.Expense, 100000, D(10, 9), Pet, PaymentMethod.Pix, "Aluguel").Value;
        world.Add(Recurrence.StartFrom(first, world.Itau, RecurrenceFrequency.Monthly, D(30, 11)).Value);

        world.Project(D(31, 12)).Select(p => p.OccurrenceDate).ShouldBe([D(10, 10), D(10, 11)]);
    }

    // Conta inativa não gera (regra 8), então não se prevê.
    [Fact]
    public void An_inactive_account_projects_nothing()
    {
        var world = PetAndRentOn20October();
        world.Itau.Update("Itaú", 0, null, null, null, isActive: false);

        world.Project(D(31, 12)).ShouldAllBe(p => p.AccountId == world.Visa.Id);
    }

    // A barra do "Já comprometido" é de despesas: a receita que se repete não entra.
    [Fact]
    public void Income_is_not_a_projected_expense()
    {
        var world = new World();
        var salary = Transaction.CreateSimple(UserId, world.Itau, TransactionType.Income, 500000, D(5, 10), Salary, PaymentMethod.Ted, "Salário").Value;
        world.Add(Recurrence.StartFrom(salary, world.Itau, RecurrenceFrequency.Monthly, null).Value);

        world.Project(D(30, 11)).Single().Type.ShouldBe(TransactionType.Income);
        world.Committed(D(20, 10)).Months[0].ProjectedExpenseCents.ShouldBe(0);
    }

    // A cobrança que cairia numa fatura paga vira pendência, não lançamento (regra 9): não se prevê.
    [Fact]
    public void A_charge_that_would_land_on_a_paid_statement_is_not_projected()
    {
        var world = new World();
        world.PetSeries();
        var november = Statement.Open(UserId, world.Visa.Id, new StatementDates("2026-11", D(26, 10), D(5, 11)));
        november.MarkAsPaid();
        world.Statements.Add(november);

        world.Project(D(26, 11)).Select(p => p.OccurrenceDate).ShouldBe([D(25, 11)]);
    }

    // Toda semana, atravessando o mês: cada sexta no seu mês de caixa (Pix: o próprio dia).
    [Fact]
    public void A_weekly_series_projects_each_week_in_its_own_month()
    {
        var world = new World();
        var first = Transaction.CreateSimple(UserId, world.Itau, TransactionType.Expense, 50000, D(2, 10), Pet, PaymentMethod.Pix, "Diarista").Value;
        world.Add(Recurrence.StartFrom(first, world.Itau, RecurrenceFrequency.Weekly, null).Value);

        var committed = world.Committed(D(20, 10));

        committed.Months[0].ProjectedExpenseCents.ShouldBe(4 * 50000); // novembro: 06, 13, 20 e 27
        world.Project(D(31, 10)).Select(p => p.OccurrenceDate).ShouldBe([D(9, 10), D(16, 10), D(23, 10), D(30, 10)]);
    }
}
