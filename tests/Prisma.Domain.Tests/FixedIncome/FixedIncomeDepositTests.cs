using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O aporte (docs/renda-fixa.md, 2.3): data, valor, produto e percentual do CDI, cada um com o seu contrato. Pode estar em
// uma caixa (objetivo) ou solto. Não guarda rendimento, saldo nem imposto.
public sealed class FixedIncomeDepositTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateOnly Day = new(2026, 7, 21);

    private static Result<FixedIncomeDeposit> Create(
        long amountCents = 609_731, DateOnly? date = null, long percent = 1_000_000, DateOnly? maturity = null,
        string? note = null, Guid? goal = null, Guid? requestId = null,
        FixedIncomeProductKind product = FixedIncomeProductKind.Cdb, FixedIncomeIndexKind index = FixedIncomeIndexKind.Cdi,
        DepositLiquidity liquidity = DepositLiquidity.Unknown) =>
        FixedIncomeDeposit.Create(User, goal, date ?? Day, amountCents, product, index, percent, maturity, liquidity, note,
            requestId ?? Guid.NewGuid(), Today);

    [Fact]
    public void A_valid_deposit_keeps_what_was_typed()
    {
        var goal = Guid.NewGuid();
        var request = Guid.NewGuid();

        var deposit = Create(609_731, Day, 1_050_000, new DateOnly(2028, 7, 21), "  CDB do Itaú  ", goal, request).Value;

        deposit.UserId.ShouldBe(User);
        deposit.GoalId.ShouldBe(goal);
        deposit.DepositDate.ShouldBe(Day);
        deposit.AmountCents.ShouldBe(609_731);
        deposit.ProductKind.ShouldBe(FixedIncomeProductKind.Cdb);
        deposit.IndexKind.ShouldBe(FixedIncomeIndexKind.Cdi);
        deposit.IndexPercentE4.ShouldBe(1_050_000);
        deposit.MaturityDate.ShouldBe(new DateOnly(2028, 7, 21));
        deposit.Note.ShouldBe("CDB do Itaú");
        deposit.ClientRequestId.ShouldBe(request);
    }

    [Fact]
    public void A_deposit_can_be_loose_with_no_goal()
    {
        var deposit = Create().Value;

        deposit.GoalId.ShouldBeNull();
        deposit.MaturityDate.ShouldBeNull();
        deposit.Note.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-609_731)]
    public void The_amount_must_be_positive(long cents) => Create(cents).IsSuccess.ShouldBeFalse();

    [Fact]
    public void The_amount_has_the_ceiling_of_the_engine()
    {
        Create(1).IsSuccess.ShouldBeTrue();
        Create(FixedIncomeEngine.MaxPrincipalCents).IsSuccess.ShouldBeTrue();
        Create(FixedIncomeEngine.MaxPrincipalCents + 1).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData(IndexPercent.Min - 1, false)]
    [InlineData(IndexPercent.Min, true)]
    [InlineData(1_100_000, true)]
    [InlineData(IndexPercent.Max, true)]
    [InlineData(IndexPercent.Max + 1, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void The_percent_follows_the_range_of_the_engine(long percent, bool valid) => Create(percent: percent).IsSuccess.ShouldBe(valid);

    [Fact]
    public void The_deposit_date_cannot_be_in_the_future()
    {
        Create(date: Today).IsSuccess.ShouldBeTrue();
        Create(date: Today.AddDays(1)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void The_maturity_must_be_after_the_deposit_date()
    {
        Create(date: Day, maturity: Day.AddDays(1)).IsSuccess.ShouldBeTrue();
        Create(date: Day, maturity: Day).IsSuccess.ShouldBeFalse();
        Create(date: Day, maturity: Day.AddDays(-1)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void The_note_is_trimmed_empty_is_none_and_it_has_a_limit()
    {
        Create(note: "   ").Value.Note.ShouldBeNull();
        Create(note: new string('a', FixedIncomeDeposit.NoteMaxLength)).IsSuccess.ShouldBeTrue();
        Create(note: new string('a', FixedIncomeDeposit.NoteMaxLength + 1)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Only_the_products_the_engine_knows_are_accepted()
    {
        Create(product: (FixedIncomeProductKind)99).IsSuccess.ShouldBeFalse();
        Create(index: (FixedIncomeIndexKind)99).IsSuccess.ShouldBeFalse();
        Create(liquidity: (DepositLiquidity)99).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void A_request_id_is_required() => Create(requestId: Guid.Empty).IsSuccess.ShouldBeFalse();

    [Fact]
    public void Update_replaces_the_contract_and_a_refused_update_changes_nothing()
    {
        var deposit = Create(609_731, Day, 1_000_000).Value;

        deposit.Update(new DateOnly(2026, 8, 5), 50_000, FixedIncomeProductKind.Cdb, FixedIncomeIndexKind.Cdi, 1_100_000, null,
            DepositLiquidity.Daily, "novo", Today).ShouldBeNull();
        (deposit.DepositDate, deposit.AmountCents, deposit.IndexPercentE4, deposit.Liquidity, deposit.Note)
            .ShouldBe((new DateOnly(2026, 8, 5), 50_000, 1_100_000, DepositLiquidity.Daily, "novo"));

        deposit.Update(Today.AddDays(5), 1, FixedIncomeProductKind.Cdb, FixedIncomeIndexKind.Cdi, 1_000_000, null,
            DepositLiquidity.Unknown, null, Today).ShouldNotBeNull();
        deposit.DepositDate.ShouldBe(new DateOnly(2026, 8, 5));
        deposit.AmountCents.ShouldBe(50_000);
    }

    // Mover de caixa não toca no contrato: data, valor, percentual e rendimento seguem os mesmos.
    [Fact]
    public void Moving_between_goals_changes_only_the_goal()
    {
        var deposit = Create(609_731, Day, 1_050_000).Value;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        deposit.MoveTo(first);
        deposit.GoalId.ShouldBe(first);
        deposit.MoveTo(second);
        deposit.GoalId.ShouldBe(second);
        deposit.MoveTo(null);
        deposit.GoalId.ShouldBeNull();

        (deposit.DepositDate, deposit.AmountCents, deposit.IndexPercentE4).ShouldBe((Day, 609_731, 1_050_000));
    }

    // O vínculo com a transferência que financiou o aporte (D3): só um par de pontas, ligado ou solto. O resto do contrato não muda.
    [Fact]
    public void A_deposit_starts_without_a_funding_transfer_and_can_be_linked_and_unlinked()
    {
        var deposit = Create().Value;
        var pair = Guid.NewGuid();

        deposit.TransferPairId.ShouldBeNull();
        deposit.LinkFunding(pair);
        deposit.TransferPairId.ShouldBe(pair);
        deposit.Unlink();
        deposit.TransferPairId.ShouldBeNull();
        (deposit.DepositDate, deposit.AmountCents).ShouldBe((Day, 609_731));
    }

    [Fact]
    public void Linking_to_an_empty_pair_id_is_refused() =>
        Should.Throw<ArgumentException>(() => Create().Value.LinkFunding(Guid.Empty));
}
