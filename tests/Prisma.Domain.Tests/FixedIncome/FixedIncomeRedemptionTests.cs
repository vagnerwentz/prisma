using System.Numerics;
using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// O resgate (docs/renda-fixa-resgate.md, seções 2 e 3). Os vetores saem de um cálculo independente (Python, inteiros) sobre o
// CDI real de Cdi2026, nunca do código testado; o oráculo das propriedades é uma segunda implementação em BigInteger.
public sealed class FixedIncomeRedemptionTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly DepositDay = new(2026, 7, 21);
    private static readonly DateOnly FirstDay = new(2026, 9, 15);
    private static readonly RateHistory Rates = Cdi2026.History;

    private static FixedIncomeDeposit Deposit(
        long cents = 1_000_000, FixedIncomeProductKind product = FixedIncomeProductKind.Cdb,
        FixedIncomeIndexKind index = FixedIncomeIndexKind.Cdi, long rateE4 = 1_000_000, DateOnly? date = null) =>
        FixedIncomeDeposit.Create(User, null, date ?? DepositDay, cents, product, index, rateE4, null, DepositLiquidity.Unknown, null,
            Guid.NewGuid(), Today).Value;

    private static Result<FixedIncomeRedemption> Redeem(
        FixedIncomeDeposit deposit, DateOnly date, long gross, long? net = null,
        IReadOnlyList<FixedIncomeRedemption>? previous = null, RateHistory? rates = null, DateOnly? today = null) =>
        FixedIncomeRedemption.Create(deposit, previous ?? [], rates ?? Rates, date, gross, net, Guid.NewGuid(), today ?? Today);

    // ---------- vetores independentes: CDB 100% do CDI, R$ 10.000,00 em 21/07/2026 ----------

    [Fact]
    public void A_partial_redemption_takes_principal_and_yield_in_proportion_and_estimates_the_tax()
    {
        // Saldo em 15/09 (56 dias): 1.020.453. Resgate de R$ 3.000,00: principal round(1.000.000 × 300.000 / 1.020.453) = 293.987,
        // rendimento 6.013, IR de 22,5 % = 1.353 (arredondado), líquido 298.647.
        var redemption = Redeem(Deposit(), FirstDay, 300_000).Value;

        redemption.DepositId.ShouldNotBe(Guid.Empty);
        redemption.UserId.ShouldBe(User);
        redemption.RedemptionDate.ShouldBe(FirstDay);
        redemption.GrossCents.ShouldBe(300_000);
        redemption.PrincipalCents.ShouldBe(293_987);
        redemption.YieldCents.ShouldBe(6_013);
        redemption.IofCents.ShouldBe(0);
        redemption.IrCents.ShouldBe(1_353);
        redemption.TaxCents.ShouldBe(1_353);
        redemption.NetCents.ShouldBe(298_647);
        redemption.TaxSource.ShouldBe(RedemptionTaxSource.Estimated);
        redemption.TaxRuleVersion.ShouldBe(FixedIncomeTax.CurrentRuleVersion);
    }

    [Fact]
    public void Redeeming_the_rest_takes_all_the_remaining_principal_with_no_leftover_cent()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;

        // Em 16/10 (87 dias) o que sobra (706.013 de principal) vale 728.559; resgatar tudo leva o principal inteiro.
        var second = Redeem(deposit, new DateOnly(2026, 10, 16), 728_559, previous: [first]).Value;

        second.PrincipalCents.ShouldBe(706_013);
        second.YieldCents.ShouldBe(22_546);
        second.IrCents.ShouldBe(5_073);
        second.NetCents.ShouldBe(723_486);
        (first.PrincipalCents + second.PrincipalCents).ShouldBe(1_000_000);
    }

    [Fact]
    public void After_all_is_redeemed_nothing_is_left_to_redeem()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;
        var second = Redeem(deposit, new DateOnly(2026, 10, 16), 728_559, previous: [first]).Value;

        var third = Redeem(deposit, new DateOnly(2026, 10, 16), 1, previous: [first, second]);

        third.IsSuccess.ShouldBeFalse();
        third.Error!.Message.ShouldBe("Esse aporte já foi resgatado por inteiro.");
    }

    [Fact]
    public void The_balance_and_the_series_drop_on_the_day_of_the_redemption()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;
        var steps = new[] { new RedemptionStep(first.RedemptionDate, new Money(first.PrincipalCents)) };
        var cdi = deposit.Remuneration;

        // 14/09 ainda sem o resgate; 15/09 já com ele (o resgate do dia conta); 16/09 com o que sobra rendendo.
        var points = FixedIncomeEngine.Series(
            new Money(1_000_000), DepositDay, cdi, Rates,
            [new DateOnly(2026, 9, 14), FirstDay, new DateOnly(2026, 9, 16)], steps).Value;
        points.Select(p => p!.Value.Cents).ToArray().ShouldBe([1_019_926L, 720_453L, 720_825L]);

        var balance = FixedIncomeEngine.Balance(new Money(1_000_000), DepositDay, cdi, Rates, FirstDay, steps).Value;
        balance.Principal.Cents.ShouldBe(706_013);
        balance.GrossBalance.Cents.ShouldBe(720_453);
        balance.GrossYield.Cents.ShouldBe(14_440);
        balance.FactorE8.ShouldBe(102_045_302);

        FixedIncomeEngine.Balance(new Money(1_000_000), DepositDay, cdi, Rates, new DateOnly(2026, 9, 14), steps).Value
            .Principal.Cents.ShouldBe(1_000_000);
    }

    [Fact]
    public void Without_redemptions_the_balance_and_the_series_are_exactly_what_they_were()
    {
        var cdi = Deposit().Remuneration;
        var dates = new[] { new DateOnly(2026, 8, 3), FirstDay, new DateOnly(2026, 10, 16) };

        FixedIncomeEngine.Balance(new Money(1_000_000), DepositDay, cdi, Rates, FirstDay, []).Value
            .ShouldBe(FixedIncomeEngine.Balance(new Money(1_000_000), DepositDay, cdi, Rates, FirstDay).Value);
        FixedIncomeEngine.Series(new Money(1_000_000), DepositDay, cdi, Rates, dates, []).Value
            .ShouldBe(FixedIncomeEngine.Series(new Money(1_000_000), DepositDay, cdi, Rates, dates).Value);
    }

    // ---------- imposto ----------

    [Fact]
    public void The_tax_bracket_is_the_one_of_the_deposit_not_of_the_previous_redemption()
    {
        // Resgate parcial aos 56 dias (22,5 %) e outro aos 87: os dois na faixa dos dias desde o APORTE, que é a mesma aqui;
        // com aporte de 2025 o resgate aos 270 dias cai na faixa de 20 % mesmo que o anterior tenha sido aos 100.
        var history = RateHistory.Create(Enumerable.Range(0, 400)
            .Select(i => new DailyRate(new DateOnly(2025, 1, 1).AddDays(i), 40_000))).Value;
        var deposit = Deposit(date: new DateOnly(2025, 1, 1));

        var early = Redeem(deposit, new DateOnly(2025, 4, 11), 500_000, rates: history, today: new DateOnly(2026, 1, 1)).Value;
        var late = Redeem(deposit, new DateOnly(2025, 9, 28), 100_000, previous: [early], rates: history, today: new DateOnly(2026, 1, 1)).Value;

        early.IrCents.ShouldBe((long)Math.Round(early.YieldCents * 0.225m, MidpointRounding.AwayFromZero));
        late.IrCents.ShouldBe((long)Math.Round(late.YieldCents * 0.20m, MidpointRounding.AwayFromZero)); // 270 dias
        late.TaxRuleVersion.ShouldBe(FixedIncomeTax.CurrentRuleVersion);
    }

    [Fact]
    public void An_lca_pays_no_tax_and_an_lci_has_no_ir()
    {
        var lca = Redeem(Deposit(product: FixedIncomeProductKind.Lca), FirstDay, 300_000).Value;
        lca.TaxCents.ShouldBe(0);
        lca.NetCents.ShouldBe(300_000);
        lca.TaxRuleVersion.ShouldBe(FixedIncomeTax.LcaRuleVersion);

        var lci = Redeem(Deposit(product: FixedIncomeProductKind.Lci), FirstDay, 300_000).Value;
        lci.IrCents.ShouldBe(0);
        lci.TaxCents.ShouldBe(0);
        lci.TaxRuleVersion.ShouldBe(FixedIncomeTax.LciRuleVersion);
    }

    [Fact]
    public void Iof_applies_when_the_redemption_is_under_thirty_days_after_the_deposit()
    {
        // Aporte em 21/07, resgate em 31/07 (10 dias): IOF de 66 % do rendimento, e o IR de 22,5 % sobre o que sobra.
        var redemption = Redeem(Deposit(), new DateOnly(2026, 7, 31), 100_000).Value;

        redemption.YieldCents.ShouldBeGreaterThan(0);
        redemption.IofCents.ShouldBe((long)Math.Round(redemption.YieldCents * 0.66m, MidpointRounding.AwayFromZero));
        redemption.IrCents.ShouldBe(
            (long)Math.Round((redemption.YieldCents - redemption.IofCents!.Value) * 0.225m, MidpointRounding.AwayFromZero));
        redemption.TaxCents.ShouldBe(redemption.IofCents.Value + redemption.IrCents!.Value);
    }

    [Fact]
    public void The_net_from_the_statement_wins_and_the_tax_is_the_difference()
    {
        var redemption = Redeem(Deposit(), FirstDay, 300_000, net: 298_700).Value;

        redemption.TaxSource.ShouldBe(RedemptionTaxSource.Informed);
        redemption.TaxCents.ShouldBe(1_300);
        redemption.NetCents.ShouldBe(298_700);
        redemption.IofCents.ShouldBeNull();
        redemption.IrCents.ShouldBeNull();
        redemption.TaxRuleVersion.ShouldBeNull();
        redemption.PrincipalCents.ShouldBe(293_987);
    }

    [Theory]
    [InlineData(300_001)] // mais que o bruto: o banco não paga mais do que resgatou
    [InlineData(293_986)] // imposto maior que o rendimento: não existe (IOF é limitado a ele e o IR incide sobre o que sobra)
    [InlineData(0)]
    [InlineData(-5)]
    public void A_net_outside_principal_and_gross_is_refused(long net)
    {
        var result = Redeem(Deposit(), FirstDay, 300_000, net: net);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Message.ShouldBe("O líquido informado precisa estar entre R$ 2.939,87 e R$ 3.000,00.");
    }

    [Theory]
    [InlineData(293_987)] // imposto = todo o rendimento: o limite
    [InlineData(300_000)] // sem imposto
    public void The_net_at_the_limits_is_accepted(long net) =>
        Redeem(Deposit(), FirstDay, 300_000, net: net).IsSuccess.ShouldBeTrue();

    [Fact]
    public void A_partial_redemption_never_takes_all_the_principal_so_the_rest_does_not_vanish()
    {
        // Um centavo a 100 % ao ano por mais de três anos vale vários centavos: tirar quase tudo arredondaria o principal que sai
        // para o centavo inteiro e o que sobra (só rendimento) sumiria do saldo.
        var deposit = Deposit(cents: 1, index: FixedIncomeIndexKind.Prefixed, rateE4: 1_000_000, date: new DateOnly(2023, 1, 2));
        var empty = RateHistory.Create([]).Value;
        var date = new DateOnly(2026, 10, 16);
        var balance = FixedIncomeEngine.Balance(new Money(1), deposit.DepositDate, deposit.Remuneration, empty, date).Value;
        balance.GrossBalance.Cents.ShouldBeGreaterThan(2);

        var redemption = Redeem(deposit, date, balance.GrossBalance.Cents - 1, rates: empty).Value;

        redemption.PrincipalCents.ShouldBe(0);
        redemption.YieldCents.ShouldBe(balance.GrossBalance.Cents - 1);
        // O que sobra continua valendo: o centavo de principal fica, com o fator todo, e não zero (a diferença para saldo − resgate é
        // o limite do principal inteiro: até F/2 centavos, só com principal de poucos centavos).
        var steps = new[] { new RedemptionStep(date, new Money(redemption.PrincipalCents)) };
        FixedIncomeEngine.Balance(new Money(1), deposit.DepositDate, deposit.Remuneration, empty, date, steps).Value
            .GrossBalance.Cents.ShouldBe(balance.GrossBalance.Cents);
    }

    // ---------- excluir ----------

    [Fact]
    public void Only_the_last_redemption_of_a_deposit_can_be_deleted()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;
        var second = Redeem(deposit, FirstDay.AddDays(10), 100_000, previous: [first]).Value;
        var live = new[] { second, first };

        second.CanBeDeleted(live).ShouldBeNull();
        var refused = first.CanBeDeleted(live);
        refused!.Code.ShouldBe("RedemptionNotLast");
        refused.Type.ShouldBe(ErrorType.Conflict);
        // Excluído o último, o anterior passa a ser o último.
        first.CanBeDeleted([first]).ShouldBeNull();
    }

    // ---------- recusas ----------

    [Fact]
    public void The_date_cannot_be_in_the_future_nor_before_the_deposit()
    {
        Redeem(Deposit(), Today.AddDays(1), 1).Error!.Message.ShouldBe("A data do resgate não pode estar no futuro.");
        Redeem(Deposit(), DepositDay.AddDays(-1), 1).Error!.Message.ShouldBe("A data do resgate é anterior à data do aporte.");
    }

    [Fact]
    public void The_date_cannot_be_before_the_last_redemption_of_the_deposit()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;

        var result = Redeem(deposit, FirstDay.AddDays(-1), 1_000, previous: [first]);

        result.Error!.Message.ShouldBe("O resgate não pode ser anterior ao último resgate desse aporte (15/09/2026).");
    }

    [Fact]
    public void Two_redemptions_on_the_same_day_are_valid_and_the_second_sees_the_first()
    {
        var deposit = Deposit();
        var first = Redeem(deposit, FirstDay, 300_000).Value;

        var second = Redeem(deposit, FirstDay, 100_000, previous: [first]);

        second.IsSuccess.ShouldBeTrue();
        // O saldo antes do segundo já é o do que sobrou: 720.453.
        Redeem(deposit, FirstDay, 720_454, previous: [first]).Error!.Message
            .ShouldBe("O valor do resgate precisa estar entre R$ 0,01 e o saldo de R$ 7.204,53.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_020_454)] // 1 centavo acima do saldo
    public void The_gross_is_between_one_cent_and_the_balance(long gross)
    {
        var result = Redeem(Deposit(), FirstDay, gross);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Message.ShouldBe("O valor do resgate precisa estar entre R$ 0,01 e o saldo de R$ 10.204,53.");
    }

    [Fact]
    public void The_whole_balance_and_one_cent_are_accepted()
    {
        Redeem(Deposit(), FirstDay, 1_020_453).Value.PrincipalCents.ShouldBe(1_000_000);
        Redeem(Deposit(), FirstDay, 1).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_cdi_redemption_needs_the_rates_up_to_the_business_day_before()
    {
        // As taxas vão até 15/10 (quinta); resgate em 16/10 (sexta) exige a de 15/10, e em 19/10 (segunda) exigiria a de 16/10.
        var friday = new DateOnly(2026, 10, 16);
        Redeem(Deposit(), friday, 1_000).IsSuccess.ShouldBeTrue();

        var monday = Redeem(Deposit(), new DateOnly(2026, 10, 19), 1_000, today: new DateOnly(2026, 10, 19));
        monday.Error!.Code.ShouldBe("RatesUnavailable");
        monday.Error.Message.ShouldBe("As taxas do CDI ainda não chegaram até essa data.");

        var cdiPlus = Redeem(Deposit(index: FixedIncomeIndexKind.CdiPlus, rateE4: 20_000), new DateOnly(2026, 10, 19), 1_000,
            today: new DateOnly(2026, 10, 19));
        cdiPlus.Error!.Code.ShouldBe("RatesUnavailable");
    }

    [Fact]
    public void A_prefixed_redemption_does_not_depend_on_the_published_rates()
    {
        var deposit = Deposit(index: FixedIncomeIndexKind.Prefixed, rateE4: 140_000);
        var empty = RateHistory.Create([]).Value;

        var result = Redeem(deposit, new DateOnly(2026, 10, 19), 1_000, rates: empty, today: new DateOnly(2026, 10, 19));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_request_without_an_identifier_is_refused()
    {
        var result = FixedIncomeRedemption.Create(Deposit(), [], Rates, FirstDay, 1_000, null, Guid.Empty, Today);

        result.Error!.Message.ShouldBe("O pedido veio sem identificador.");
    }

    // ---------- propriedades, contra o oráculo em BigInteger ----------

    private static readonly DateOnly Start = new(2026, 1, 2);

    private static (BigInteger Gross, BigInteger Principal) Oracle(BigInteger remaining, BigInteger factorE8, BigInteger x)
    {
        var s = (remaining * factorE8 + 50_000_000) / 100_000_000;
        var principal = x == s ? remaining : (remaining * x * 2 + s) / (2 * s);
        if (x < s && principal > remaining - 1)
            principal = remaining - 1 < 0 ? 0 : remaining - 1;
        return (s, principal);
    }

    [Fact]
    public void A_chain_of_redemptions_matches_the_oracle_and_never_loses_a_cent()
    {
        // Taxa constante de 0,05 % ao dia por 200 dias a partir de 02/01/2026.
        var history = RateHistory.Create(Enumerable.Range(0, 200).Select(i => new DailyRate(Start.AddDays(i), 50_000))).Value;
        var today = Start.AddDays(201);

        Gen.Select(Gen.Long[1, 10_000_000_00], Gen.Int[0, 60].List[1, 6], Gen.Double[0.001, 1.0].List[1, 6])
            .Sample((cents, gaps, shares) =>
            {
                var depositDate = Start;
                var deposit = FixedIncomeDeposit.Create(User, null, depositDate, cents, FixedIncomeProductKind.Cdb,
                    FixedIncomeIndexKind.Cdi, 1_000_000, null, DepositLiquidity.Unknown, null, Guid.NewGuid(), today).Value;

                var previous = new List<FixedIncomeRedemption>();
                var date = depositDate;
                BigInteger remaining = cents;
                for (var i = 0; i < Math.Min(gaps.Count, shares.Count); i++)
                {
                    date = date.AddDays(gaps[i]);
                    if (date > Start.AddDays(199))
                        break;

                    var balance = FixedIncomeEngine.Balance(
                        new Money(cents), depositDate, deposit.Remuneration, history, date,
                        previous.Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList()).Value;
                    if (balance.Principal.Cents == 0)
                        break;

                    var x = Math.Max(1, (long)(balance.GrossBalance.Cents * shares[i]));
                    var result = FixedIncomeRedemption.Create(deposit, previous, history, date, x, null, Guid.NewGuid(), today);
                    if (!result.IsSuccess)
                        return false;

                    var (s, principal) = Oracle(remaining, balance.FactorE8, x);
                    var redemption = result.Value;
                    if (s != balance.GrossBalance.Cents || redemption.PrincipalCents != (long)principal
                        || redemption.YieldCents != x - (long)principal || redemption.NetCents != x - redemption.TaxCents
                        || redemption.TaxCents > redemption.YieldCents || redemption.PrincipalCents < 0
                        || redemption.YieldCents < 0)
                        return false;

                    // Continuidade: o saldo do que sobra bate com saldo − resgate, a menos de 1 centavo.
                    remaining -= principal;
                    var after = FixedIncomeEngine.Balance(
                        new Money(cents), depositDate, deposit.Remuneration, history, date,
                        previous.Append(redemption).Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList()).Value;
                    if (after.Principal.Cents != (long)remaining
                        || Math.Abs(after.GrossBalance.Cents - (balance.GrossBalance.Cents - x)) > 1 && after.GrossBalance.Cents != 0)
                        return false;

                    previous.Add(redemption);
                }

                // Principal que saiu + principal que sobra = aporte, sempre.
                return previous.Sum(r => r.PrincipalCents) + (long)remaining == cents;
            }, iter: 2_000);
    }

    [Fact]
    public void Redeeming_the_whole_balance_always_leaves_zero()
    {
        var history = RateHistory.Create(Enumerable.Range(0, 200).Select(i => new DailyRate(Start.AddDays(i), 50_000))).Value;
        var today = Start.AddDays(201);

        Gen.Select(Gen.Long[1, 10_000_000_00], Gen.Int[1, 190], Gen.Double[0.0, 0.999])
            .Sample((cents, days, firstShare) =>
            {
                var deposit = FixedIncomeDeposit.Create(User, null, Start, cents, FixedIncomeProductKind.Lca,
                    FixedIncomeIndexKind.Cdi, 1_000_000, null, DepositLiquidity.Unknown, null, Guid.NewGuid(), today).Value;
                var previous = new List<FixedIncomeRedemption>();
                var half = Start.AddDays(days / 2);
                var balance = FixedIncomeEngine.Balance(new Money(cents), Start, deposit.Remuneration, history, half).Value;
                var x = (long)(balance.GrossBalance.Cents * firstShare);
                if (x >= 1)
                    previous.Add(FixedIncomeRedemption.Create(deposit, [], history, half, x, null, Guid.NewGuid(), today).Value);

                var end = Start.AddDays(days);
                var steps = previous.Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList();
                var rest = FixedIncomeEngine.Balance(new Money(cents), Start, deposit.Remuneration, history, end, steps).Value;
                if (rest.Principal.Cents == 0)
                    return true;

                var last = FixedIncomeRedemption.Create(deposit, previous, history, end, rest.GrossBalance.Cents, null, Guid.NewGuid(), today).Value;
                return last.PrincipalCents == rest.Principal.Cents
                    && FixedIncomeEngine.Balance(
                        new Money(cents), Start, deposit.Remuneration, history, end,
                        steps.Append(new RedemptionStep(end, new Money(last.PrincipalCents))).ToList()).Value.GrossBalance.Cents == 0;
            }, iter: 2_000);
    }

    [Fact]
    public void Series_points_equal_the_balance_for_the_same_date_with_redemptions()
    {
        var history = RateHistory.Create(Enumerable.Range(0, 200).Select(i => new DailyRate(Start.AddDays(i), 50_000))).Value;

        Gen.Select(Gen.Long[1, 10_000_000_00], Gen.Int[0, 190].List[1, 8], Gen.Long[1, 5_000].List[0, 3])
            .Sample((cents, offsets, parts) =>
            {
                var dates = offsets.Distinct().Order().Select(o => Start.AddDays(o)).ToList();
                var steps = parts.Select((p, i) => new RedemptionStep(Start.AddDays(10 + i * 20), new Money(Math.Min(p, cents / 4)))).Where(s => s.Principal.Cents > 0).ToList();
                var cdi = Remuneration.PercentOfCdi(new IndexPercent(1_000_000));

                var series = FixedIncomeEngine.Series(new Money(cents), Start, cdi, history, dates, steps).Value;
                return dates.Select((d, i) =>
                    FixedIncomeEngine.Balance(new Money(cents), Start, cdi, history, d, steps).Value.GrossBalance == series[i]).All(ok => ok);
            }, iter: 1_000);
    }

    [Fact]
    public void Steps_that_do_not_fit_the_deposit_are_refused()
    {
        var cdi = Deposit().Remuneration;

        FixedIncomeEngine.Balance(new Money(1_000), DepositDay, cdi, Rates, FirstDay,
            [new RedemptionStep(FirstDay, new Money(1_001))]).IsSuccess.ShouldBeFalse();
        FixedIncomeEngine.Balance(new Money(1_000), DepositDay, cdi, Rates, FirstDay,
            [new RedemptionStep(DepositDay.AddDays(-1), new Money(10))]).IsSuccess.ShouldBeFalse();
        FixedIncomeEngine.Balance(new Money(1_000), DepositDay, cdi, Rates, FirstDay,
            [new RedemptionStep(FirstDay, new Money(-1))]).IsSuccess.ShouldBeFalse();
        FixedIncomeEngine.Balance(new Money(1_000), DepositDay, cdi, Rates, FirstDay,
            [new RedemptionStep(FirstDay, new Money(0))]).IsSuccess.ShouldBeTrue();
    }
}
