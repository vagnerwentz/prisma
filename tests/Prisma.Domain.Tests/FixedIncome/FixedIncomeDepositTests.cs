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
        deposit.RateE4.ShouldBe(1_050_000);
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

    // A taxa guardada é uma só e o índice diz o que ela é (docs/renda-fixa.md, 5.5): no CDI, o percentual (até 500 %); no CDI+ e
    // no pré, a taxa ao ano (até 100 %). O mesmo número vale ou não conforme o índice.
    [Theory]
    [InlineData(FixedIncomeIndexKind.Cdi, AnnualRate.Max + 1, true)]
    [InlineData(FixedIncomeIndexKind.Cdi, IndexPercent.Max, true)]
    [InlineData(FixedIncomeIndexKind.Cdi, IndexPercent.Max + 1, false)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, AnnualRate.Min - 1, false)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, AnnualRate.Min, true)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, 20_000, true)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, AnnualRate.Max, true)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, AnnualRate.Max + 1, false)]
    [InlineData(FixedIncomeIndexKind.Prefixed, 0, false)]
    [InlineData(FixedIncomeIndexKind.Prefixed, 140_000, true)]
    [InlineData(FixedIncomeIndexKind.Prefixed, AnnualRate.Max, true)]
    [InlineData(FixedIncomeIndexKind.Prefixed, AnnualRate.Max + 1, false)]
    [InlineData(FixedIncomeIndexKind.Prefixed, 1_100_000, false)] // 110 %: serve de percentual do CDI, não de taxa ao ano
    public void The_rate_follows_the_range_of_its_index(FixedIncomeIndexKind index, long rate, bool valid) =>
        Create(percent: rate, index: index).IsSuccess.ShouldBe(valid);

    [Fact]
    public void The_refusal_says_which_rate_is_out_of_range()
    {
        Create(percent: 0, index: FixedIncomeIndexKind.Cdi).Error!.Message.ShouldContain("percentual do CDI");
        Create(percent: 0, index: FixedIncomeIndexKind.Prefixed).Error!.Message.ShouldContain("taxa ao ano");
    }

    [Theory]
    [InlineData(FixedIncomeIndexKind.Cdi, 1_100_000)]
    [InlineData(FixedIncomeIndexKind.CdiPlus, 20_000)]
    [InlineData(FixedIncomeIndexKind.Prefixed, 140_000)]
    public void The_deposit_reads_its_rate_through_the_remuneration(FixedIncomeIndexKind index, long rate)
    {
        var remuneration = Create(percent: rate, index: index).Value.Remuneration;

        remuneration.Kind.ShouldBe(index);
        remuneration.ValueE4.ShouldBe(rate);
        remuneration.IsInRange.ShouldBeTrue();
    }

    [Fact]
    public void Update_can_change_the_kind_and_the_rate_together()
    {
        var deposit = Create(609_731, Day, 1_000_000).Value;

        deposit.Update(Day, 609_731, FixedIncomeProductKind.Cdb, FixedIncomeIndexKind.Prefixed, 140_000, null,
            DepositLiquidity.Unknown, null, Today).ShouldBeNull();
        (deposit.IndexKind, deposit.RateE4).ShouldBe((FixedIncomeIndexKind.Prefixed, 140_000));

        // 600 % cabe no CDI e não cabe numa taxa ao ano: a troca de índice com o número velho é recusada e nada muda.
        deposit.Update(Day, 609_731, FixedIncomeProductKind.Cdb, FixedIncomeIndexKind.CdiPlus, 6_000_000, null,
            DepositLiquidity.Unknown, null, Today).ShouldNotBeNull();
        (deposit.IndexKind, deposit.RateE4).ShouldBe((FixedIncomeIndexKind.Prefixed, 140_000));
    }

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

    [Theory]
    [InlineData(FixedIncomeProductKind.Cdb)]
    [InlineData(FixedIncomeProductKind.Lci)]
    [InlineData(FixedIncomeProductKind.Lca)]
    public void Cdb_lci_and_lca_are_accepted_with_any_index(FixedIncomeProductKind product)
    {
        foreach (var (index, rate) in new[] { (FixedIncomeIndexKind.Cdi, 900_000L), (FixedIncomeIndexKind.CdiPlus, 20_000L), (FixedIncomeIndexKind.Prefixed, 110_000L) })
        {
            var deposit = Create(percent: rate, index: index, product: product).Value;

            deposit.ProductKind.ShouldBe(product);
            deposit.IndexKind.ShouldBe(index);
        }
    }

    [Fact]
    public void Update_can_change_the_product()
    {
        var deposit = Create(609_731, Day).Value;

        deposit.Update(Day, 609_731, FixedIncomeProductKind.Lci, FixedIncomeIndexKind.Cdi, 900_000, null, DepositLiquidity.Unknown, null, Today)
            .ShouldBeNull();

        deposit.ProductKind.ShouldBe(FixedIncomeProductKind.Lci);
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
        (deposit.DepositDate, deposit.AmountCents, deposit.RateE4, deposit.Liquidity, deposit.Note)
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

        (deposit.DepositDate, deposit.AmountCents, deposit.RateE4).ShouldBe((Day, 609_731, 1_050_000));
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
