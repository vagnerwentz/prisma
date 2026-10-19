using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// Imposto estimado do CDB se o aporte fosse resgatado numa data (docs/renda-fixa.md, 6.4). Os valores esperados saem da regra,
// feitos à mão: IR regressivo (Lei 11.033/2004, art. 1º) de 22,5 / 20 / 17,5 / 15 % por faixa de dias corridos, sobre o
// rendimento já líquido do IOF; IOF pela tabela do Decreto 6.306/2007 para menos de 30 dias, como % do rendimento.
public sealed class FixedIncomeTaxTests
{
    private static readonly DateOnly Deposit = new(2026, 1, 10);

    private static TaxEstimate Estimate(long yieldCents, int days) =>
        FixedIncomeTax.Estimate(FixedIncomeProductKind.Cdb, Deposit, Deposit.AddDays(days), new Money(yieldCents)).Value;

    // ---- IR: as bordas das faixas (R$ 1.000,00 de rendimento, sem IOF desde o dia 30) ----

    [Theory]
    [InlineData(30, 22_500, 2_250)]
    [InlineData(180, 22_500, 2_250)]
    [InlineData(181, 20_000, 2_000)]
    [InlineData(360, 20_000, 2_000)]
    [InlineData(361, 17_500, 1_750)]
    [InlineData(720, 17_500, 1_750)]
    [InlineData(721, 15_000, 1_500)]
    [InlineData(3_650, 15_000, 1_500)]
    public void Ir_bracket_edges(int days, long expectedIrCents, int expectedRateBasisPoints)
    {
        var estimate = Estimate(100_000, days);

        estimate.IofCents.ShouldBe(0);
        estimate.IrCents.ShouldBe(expectedIrCents);
        estimate.IrRateBasisPoints.ShouldBe(expectedRateBasisPoints);
        estimate.HoldingDays.ShouldBe(days);
    }

    // ---- IOF: tabela de 1 a 29 dias, depois zero ----

    [Theory]
    [InlineData(1, 96)]
    [InlineData(2, 93)]
    [InlineData(10, 66)]
    [InlineData(15, 50)]
    [InlineData(20, 33)]
    [InlineData(28, 6)]
    [InlineData(29, 3)]
    [InlineData(30, 0)]
    public void Iof_follows_the_table_as_a_share_of_the_yield(int days, int expectedPercent)
    {
        // R$ 1.000,00 de rendimento: a % da tabela vira centavos exatos.
        Estimate(100_000, days).IofCents.ShouldBe(expectedPercent * 1_000);
    }

    [Fact]
    public void Ir_is_charged_on_the_yield_after_iof()
    {
        // 1 dia: IOF 96% de R$ 100,00 = R$ 96,00; sobram R$ 4,00; IR de 22,5% sobre isso = R$ 0,90.
        var estimate = Estimate(10_000, 1);

        estimate.IofCents.ShouldBe(9_600);
        estimate.IrCents.ShouldBe(90);
        estimate.TotalCents.ShouldBe(9_690);
    }

    [Fact]
    public void Ir_after_iof_at_ten_days()
    {
        // 10 dias: IOF 66% de R$ 100,00 = R$ 66,00; sobram R$ 34,00; IR de 22,5% = R$ 7,65.
        var estimate = Estimate(10_000, 10);

        estimate.IofCents.ShouldBe(6_600);
        estimate.IrCents.ShouldBe(765);
    }

    [Fact]
    public void Day_29_rounds_half_a_cent_up()
    {
        // IOF 3% de R$ 100,00 = R$ 3,00; sobram R$ 97,00; IR 22,5% = 21,825 → R$ 21,83 (meio centavo sobe, como nos saldos).
        var estimate = Estimate(10_000, 29);

        estimate.IofCents.ShouldBe(300);
        estimate.IrCents.ShouldBe(2_183);
    }

    // ---- Arredondamento ----

    [Theory]
    [InlineData(2, 0)] // 0,45 desce
    [InlineData(3, 1)] // 0,675 sobe
    [InlineData(1, 0)] // 0,225 desce
    [InlineData(200, 45)] // 45,0 exato
    [InlineData(222, 50)] // 49,95 sobe
    public void Ir_rounds_half_a_cent_up(long yieldCents, long expectedIrCents) =>
        Estimate(yieldCents, 100).IrCents.ShouldBe(expectedIrCents);

    [Theory]
    [InlineData(49, 1)] // 3% de 49 = 1,47 desce
    [InlineData(50, 2)] // 3% de 50 = 1,5 sobe
    public void Iof_rounds_half_a_cent_up(long yieldCents, long expectedIofCents) =>
        Estimate(yieldCents, 29).IofCents.ShouldBe(expectedIofCents);

    // ---- Sem rendimento, sem imposto ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    [InlineData(-500, 100)]
    public void No_yield_means_no_tax(long yieldCents, int days)
    {
        var estimate = Estimate(yieldCents, days);

        estimate.IofCents.ShouldBe(0);
        estimate.IrCents.ShouldBe(0);
        estimate.TotalCents.ShouldBe(0);
    }

    [Fact]
    public void Redeeming_on_the_deposit_day_has_no_yield_and_no_tax() =>
        Estimate(0, 0).TotalCents.ShouldBe(0);

    // ---- LCI e LCA (docs/renda-fixa.md, 6.5): isentas de IR para pessoa física (Lei 11.033/2004, art. 3º, II para a LCI e IV para a LCA) ----

    private static TaxEstimate Estimate(FixedIncomeProductKind product, long yieldCents, int days) =>
        FixedIncomeTax.Estimate(product, Deposit, Deposit.AddDays(days), new Money(yieldCents)).Value;

    [Theory]
    [InlineData(FixedIncomeProductKind.Lci)]
    [InlineData(FixedIncomeProductKind.Lca)]
    public void The_letters_pay_no_ir_in_any_bracket(FixedIncomeProductKind product)
    {
        foreach (var days in new[] { 30, 180, 181, 360, 361, 720, 721, 3_650 })
        {
            var estimate = Estimate(product, 100_000, days);

            estimate.IrCents.ShouldBe(0, $"{product} em {days} dias");
            estimate.IrRateBasisPoints.ShouldBe(0, $"{product} em {days} dias");
            estimate.IofCents.ShouldBe(0, $"{product} em {days} dias");
            estimate.TotalCents.ShouldBe(0, $"{product} em {days} dias");
            estimate.HoldingDays.ShouldBe(days);
        }
    }

    // A LCI não tem alíquota zero de IOF no Decreto 6.306 (art. 32, § 2º): vale a mesma tabela do CDB abaixo de 30 dias, e o IR segue
    // isento (o IOF não entra na base de um IR que não existe).
    [Theory]
    [InlineData(1, 96)]
    [InlineData(15, 50)]
    [InlineData(29, 3)]
    [InlineData(30, 0)]
    public void The_lci_pays_the_cdb_iof_table_under_30_days_and_no_ir(int days, int expectedPercent)
    {
        var estimate = Estimate(FixedIncomeProductKind.Lci, 100_000, days);

        estimate.IofCents.ShouldBe(expectedPercent * 1_000);
        estimate.IrCents.ShouldBe(0);
        estimate.TotalCents.ShouldBe(expectedPercent * 1_000);
    }

    // A LCA tem alíquota zero de IOF (Decreto 6.306, art. 32, § 2º, V): nem no primeiro dia.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(29)]
    public void The_lca_pays_no_iof_even_on_the_first_days(int days) =>
        Estimate(FixedIncomeProductKind.Lca, 100_000, days).TotalCents.ShouldBe(0);

    [Fact]
    public void The_same_yield_is_taxed_in_the_cdb_and_not_in_the_letters()
    {
        Estimate(FixedIncomeProductKind.Cdb, 100_000, 400).IrCents.ShouldBe(17_500);
        Estimate(FixedIncomeProductKind.Lci, 100_000, 400).IrCents.ShouldBe(0);
        Estimate(FixedIncomeProductKind.Lca, 100_000, 400).IrCents.ShouldBe(0);
    }

    [Fact]
    public void The_net_of_a_letter_after_30_days_is_the_gross()
    {
        var gross = new Money(1_050_000);
        var yield = new Money(50_000);

        foreach (var product in new[] { FixedIncomeProductKind.Lci, FixedIncomeProductKind.Lca })
        {
            var tax = FixedIncomeTax.Estimate(product, Deposit, Deposit.AddDays(200), yield).Value;
            var (netYield, netBalance) = FixedIncomeTax.Net(gross, yield, tax);

            netYield.ShouldBe(yield);
            netBalance.ShouldBe(gross);
        }
    }

    [Theory]
    [InlineData(FixedIncomeProductKind.Lci, 0, 1)]
    [InlineData(FixedIncomeProductKind.Lci, -500, 100)]
    [InlineData(FixedIncomeProductKind.Lca, 0, 100)]
    public void A_letter_without_yield_has_no_tax(FixedIncomeProductKind product, long yieldCents, int days) =>
        Estimate(product, yieldCents, days).TotalCents.ShouldBe(0);

    [Fact]
    public void Each_product_reports_its_own_rule_version()
    {
        Estimate(FixedIncomeProductKind.Cdb, 100_000, 100).RuleVersion.ShouldBe("cdb-lei-11033-2005");
        Estimate(FixedIncomeProductKind.Lci, 100_000, 100).RuleVersion.ShouldBe("lci-lei-11033-2005");
        Estimate(FixedIncomeProductKind.Lca, 100_000, 100).RuleVersion.ShouldBe("lca-lei-11033-2005");
    }

    [Theory]
    [InlineData(FixedIncomeProductKind.Lci)]
    [InlineData(FixedIncomeProductKind.Lca)]
    public void The_letters_rules_start_on_the_first_day_of_2005_like_the_cdb(FixedIncomeProductKind product)
    {
        FixedIncomeTax.Estimate(product, new DateOnly(2004, 6, 1), new DateOnly(2004, 12, 31), new Money(1_000)).IsSuccess.ShouldBeFalse();
        FixedIncomeTax.Estimate(product, new DateOnly(2004, 6, 1), new DateOnly(2005, 1, 1), new Money(1_000)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_product_without_a_tax_rule_is_refused_not_taxed_as_a_cdb()
    {
        var result = FixedIncomeTax.Estimate((FixedIncomeProductKind)99, Deposit, Deposit.AddDays(100), new Money(100_000));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    // ---- Entradas inválidas e regra por data ----

    [Fact]
    public void Redemption_before_the_deposit_is_refused()
    {
        var result = FixedIncomeTax.Estimate(FixedIncomeProductKind.Cdb, Deposit, Deposit.AddDays(-1), new Money(100));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Rule_version_is_reported_and_follows_the_redemption_date()
    {
        var estimate = Estimate(100_000, 100);

        estimate.RuleVersion.ShouldBe(FixedIncomeTax.CurrentRuleVersion);
        estimate.RuleVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Redemption_before_any_known_rule_is_refused()
    {
        // O IR regressivo vale desde 1º/1/2005 (Lei 11.033/2004): antes disso não há regra para estimar.
        var result = FixedIncomeTax.Estimate(
            FixedIncomeProductKind.Cdb, new DateOnly(2004, 6, 1), new DateOnly(2004, 12, 31), new Money(1_000));

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void The_rule_starts_on_the_first_day_of_2005()
    {
        var onTheDay = FixedIncomeTax.Estimate(
            FixedIncomeProductKind.Cdb, new DateOnly(2004, 6, 1), new DateOnly(2005, 1, 1), new Money(1_000));
        var dayBefore = FixedIncomeTax.Estimate(
            FixedIncomeProductKind.Cdb, new DateOnly(2004, 6, 1), new DateOnly(2004, 12, 31), new Money(1_000));

        onTheDay.IsSuccess.ShouldBeTrue();
        onTheDay.Value.RuleVersion.ShouldBe(FixedIncomeTax.CurrentRuleVersion);
        dayBefore.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Net_amounts_subtract_the_total_tax()
    {
        var estimate = Estimate(10_000, 1);

        FixedIncomeTax.Net(new Money(510_000), new Money(10_000), estimate).ShouldBe((new Money(10_000 - 9_690), new Money(510_000 - 9_690)));
    }

    // ---- Propriedades: para qualquer rendimento e prazo ----

    [Fact]
    public void Tax_never_exceeds_the_yield_and_ir_never_grows_with_time()
    {
        Gen.Long[0, 100_000_000_000].Select(Gen.Int[0, 4_000]).Sample(
            (yieldCents, days) =>
            {
                var estimate = Estimate(yieldCents, days);

                estimate.IofCents.ShouldBeInRange(0, yieldCents);
                estimate.IrCents.ShouldBeInRange(0, yieldCents - estimate.IofCents);
                estimate.TotalCents.ShouldBeLessThanOrEqualTo(yieldCents);
                if (days >= 30)
                    estimate.IofCents.ShouldBe(0);
            },
            iter: 20_000);
    }

    [Fact]
    public void Ir_rate_never_rises_as_the_holding_period_grows()
    {
        Gen.Int[30, 3_999].Sample(
            days => Estimate(1_000_000, days + 1).IrRateBasisPoints.ShouldBeLessThanOrEqualTo(Estimate(1_000_000, days).IrRateBasisPoints),
            iter: 4_000);
    }
}
