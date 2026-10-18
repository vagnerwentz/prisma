using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// Motor do rendimento de um aporte em CDB pós-fixado (docs/renda-fixa.md, seções 5.1 e 5.2). Os valores esperados vêm
// de fora do código: cálculo independente (Python, Decimal, regra da B3) e extratos do banco do dono.
public sealed class FixedIncomeEngineTests
{
    private static readonly DateOnly Reference = new(2026, 10, 16);

    private static readonly IndexPercent Cdi100 = new(1_000_000);

    private static FixedIncomeBalance Balance(long cents, DateOnly deposit, DateOnly asOf, IndexPercent? percent = null) =>
        FixedIncomeEngine.Balance(new Money(cents), deposit, percent ?? Cdi100, Cdi2026.History, asOf).Value;

    private static RateHistory History(params (int Day, long RateE8)[] october) =>
        RateHistory.Create(october.Select(r => new DailyRate(new DateOnly(2026, 10, r.Day), r.RateE8))).Value;

    // ---- Vetores sem rede ----

    // R$ 1.000,00 a 110% do CDI por 3 dias com 0,050788 % ao dia: cada dia 1 + 1,10 × 0,00050788 = 1,000558668;
    // acumulado e truncado a 16 casas = 1,0016769405041684 → fator de 8 casas 1,00167694 → R$ 1.001,68.
    [Fact]
    public void Three_days_at_110_percent_match_the_hand_calculation()
    {
        var history = History((13, 50_788), (14, 50_788), (15, 50_788));

        var result = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 10, 13), new IndexPercent(1_100_000), history, new DateOnly(2026, 10, 16));

        result.IsSuccess.ShouldBeTrue();
        result.Value.FactorE8.ShouldBe(100_167_694);
        result.Value.GrossBalance.ShouldBe(new Money(100_168));
        result.Value.GrossYield.ShouldBe(new Money(168));
        result.Value.RatesUsed.ShouldBe(3);
        result.Value.RatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
    }

    // Um dia a 0,5 % (fração 0,005): R$ 1,00 vira R$ 1,005, e o meio centavo sobe; R$ 0,99 vira 0,99495 e desce.
    [Theory]
    [InlineData(100, 101)]
    [InlineData(99, 99)]
    [InlineData(300, 302)] // 301,5
    [InlineData(1, 1)] // 1,005
    public void Half_cents_round_up(long principal, long expected)
    {
        var result = FixedIncomeEngine.Balance(
            new Money(principal), new DateOnly(2026, 10, 13), Cdi100, History((13, 500_000)), new DateOnly(2026, 10, 14));

        result.Value.GrossBalance.ShouldBe(new Money(expected));
    }

    // ---- Taxas reais do BCB, resultados calculados fora (docs/renda-fixa.md, 5.2) ----

    // R$ 6.097,31 em 21/07/2026 a 100% do CDI, em quatro datas: o saldo bruto de cada uma foi calculado à parte.
    [Theory]
    [InlineData(2026, 7, 31, 8, 100_421_021, 612_298)]
    [InlineData(2026, 8, 31, 29, 101_519_630, 618_997)]
    [InlineData(2026, 9, 30, 50, 102_618_635, 625_698)]
    [InlineData(2026, 10, 16, 61, 103_193_390, 629_202)]
    public void Simulation_of_the_owner_matches_the_independent_calculation(
        int year, int month, int day, int ratesUsed, long factorE8, long grossCents)
    {
        var result = Balance(609_731, new DateOnly(2026, 7, 21), new DateOnly(year, month, day));

        result.RatesUsed.ShouldBe(ratesUsed);
        result.FactorE8.ShouldBe(factorE8);
        result.GrossBalance.ShouldBe(new Money(grossCents));
        result.Principal.ShouldBe(new Money(609_731));
    }

    // Aportes de 21/07 do extrato do banco (caso A): R$ 4.797,31 e R$ 1.300,00. Cada um é arredondado sozinho.
    [Fact]
    public void Case_A_lots_match_the_independent_calculation()
    {
        var first = Balance(479_731, new DateOnly(2026, 7, 21), Reference);
        var second = Balance(130_000, new DateOnly(2026, 7, 21), Reference);

        first.GrossBalance.ShouldBe(new Money(495_051));
        second.GrossBalance.ShouldBe(new Money(134_151));
        first.FactorE8.ShouldBe(103_193_390);
    }

    // Caso B do extrato: R$ 10.474,08 em 22/07 (60 taxas) e R$ 9.000,00 em 10/08 (47 taxas).
    [Fact]
    public void Case_B_lots_match_the_independent_calculation()
    {
        var first = Balance(1_047_408, new DateOnly(2026, 7, 22), Reference);
        var second = Balance(900_000, new DateOnly(2026, 8, 10), Reference);

        first.RatesUsed.ShouldBe(60);
        first.FactorE8.ShouldBe(103_139_210);
        first.GrossBalance.ShouldBe(new Money(1_080_288));
        second.RatesUsed.ShouldBe(47);
        second.FactorE8.ShouldBe(102_439_236);
        second.GrossBalance.ShouldBe(new Money(921_953));
    }

    // Contra o banco (16/10/2026, IOF 0): o cálculo bate dentro de R$ 0,01 de rendimento. A diferença de um centavo
    // vem do arredondamento interno do banco, que o extrato não revela (docs/renda-fixa.md, "Calibração com o banco").
    [Fact]
    public void Case_A_yield_is_within_one_cent_of_the_bank_statement()
    {
        var totals = FixedIncomeTotals.Of([
            Balance(479_731, new DateOnly(2026, 7, 21), Reference),
            Balance(130_000, new DateOnly(2026, 7, 21), Reference),
        ]);

        // Banco: rendimento bruto R$ 194,70.
        Math.Abs(totals.GrossYield.Cents - 19_470).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Case_B_yield_is_within_one_cent_of_the_bank_statement()
    {
        var totals = FixedIncomeTotals.Of([
            Balance(1_047_408, new DateOnly(2026, 7, 22), Reference),
            Balance(900_000, new DateOnly(2026, 8, 10), Reference),
        ]);

        // Banco: rendimento bruto R$ 548,32.
        Math.Abs(totals.GrossYield.Cents - 54_832).ShouldBeLessThanOrEqualTo(1);
    }

    // Aportes com percentuais diferentes no mesmo grupo: 105% rende mais que 100%, e o fator de R$ 4.797,31 a 105% foi
    // calculado à parte.
    [Fact]
    public void A_higher_percent_of_the_cdi_yields_more()
    {
        var at100 = Balance(479_731, new DateOnly(2026, 7, 21), Reference);
        var at105 = Balance(479_731, new DateOnly(2026, 7, 21), Reference, new IndexPercent(1_050_000));

        at105.FactorE8.ShouldBe(103_355_666);
        at105.GrossBalance.ShouldBe(new Money(495_829));
        at105.GrossBalance.Cents.ShouldBeGreaterThan(at100.GrossBalance.Cents);
    }

    // ---- Datas: dia da aplicação entra, dia da referência não ----

    [Fact]
    public void The_rate_of_the_deposit_day_counts_and_the_rate_of_the_reference_day_does_not()
    {
        var history = History((13, 50_000), (14, 60_000), (15, 70_000));

        var result = FixedIncomeEngine.Balance(
            new Money(100_000_000), new DateOnly(2026, 10, 14), Cdi100, history, new DateOnly(2026, 10, 15));

        // Só a taxa do dia 14 (0,0006): 100.000.000 × 1,0006 = 100.060.000.
        result.Value.RatesUsed.ShouldBe(1);
        result.Value.GrossBalance.ShouldBe(new Money(100_060_000));
        result.Value.RatesThrough.ShouldBe(new DateOnly(2026, 10, 14));
    }

    [Fact]
    public void On_the_deposit_day_the_balance_is_the_principal()
    {
        var result = Balance(123_456, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3));

        result.GrossBalance.ShouldBe(new Money(123_456));
        result.GrossYield.ShouldBe(new Money(0));
        result.FactorE8.ShouldBe(100_000_000);
        result.RatesUsed.ShouldBe(0);
        result.RatesThrough.ShouldBeNull();
    }

    // Sexta-feira 16/10 → segunda 19/10: a taxa de sexta remunera o fim de semana inteiro; sábado e domingo não têm taxa.
    [Fact]
    public void A_friday_rate_covers_the_weekend()
    {
        var history = History((15, 50_000), (16, 50_000));

        var result = FixedIncomeEngine.Balance(
            new Money(100_000_000), new DateOnly(2026, 10, 16), Cdi100, history, new DateOnly(2026, 10, 19));

        result.Value.RatesUsed.ShouldBe(1);
        result.Value.GrossBalance.ShouldBe(new Money(100_050_000));
    }

    // Aporte no sábado: a primeira taxa é a da segunda; do sábado à segunda não rende (o dinheiro só começa a render
    // no primeiro dia com taxa publicada).
    [Fact]
    public void A_deposit_on_a_weekend_starts_on_the_next_published_rate()
    {
        var history = History((16, 50_000), (19, 50_000), (20, 50_000));

        var result = FixedIncomeEngine.Balance(
            new Money(100_000_000), new DateOnly(2026, 10, 17), Cdi100, history, new DateOnly(2026, 10, 20));

        result.Value.RatesUsed.ShouldBe(1);
        result.Value.RatesThrough.ShouldBe(new DateOnly(2026, 10, 19));
        result.Value.GrossBalance.ShouldBe(new Money(100_050_000));
    }

    // Defasagem: as taxas acabam antes da data de referência. O saldo vai até a última taxa conhecida; nada é estimado
    // para o dia que falta (quem chama usa RatesThrough para avisar).
    [Fact]
    public void When_rates_stop_early_the_balance_stops_at_the_last_known_rate()
    {
        var history = History((13, 50_000), (14, 50_000));

        var result = FixedIncomeEngine.Balance(
            new Money(100_000_000), new DateOnly(2026, 10, 13), Cdi100, history, new DateOnly(2026, 10, 30));

        result.Value.RatesUsed.ShouldBe(2);
        result.Value.RatesThrough.ShouldBe(new DateOnly(2026, 10, 14));
    }

    // ---- Recusas ----

    [Fact]
    public void A_deposit_before_the_first_known_rate_is_refused_not_treated_as_zero()
    {
        var result = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 7, 20), Cdi100, Cdi2026.History, Reference);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("RatesUnavailable");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Without_any_rate_the_balance_is_refused()
    {
        var result = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 7, 21), Cdi100, RateHistory.Create([]).Value, Reference);

        result.Error!.Code.ShouldBe("RatesUnavailable");
    }

    [Fact]
    public void A_reference_date_before_the_deposit_is_refused()
    {
        var result = FixedIncomeEngine.Balance(
            new Money(100_000), new DateOnly(2026, 8, 3), Cdi100, Cdi2026.History, new DateOnly(2026, 8, 2));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100_000)]
    public void A_principal_that_is_not_positive_is_refused(long cents) =>
        FixedIncomeEngine.Balance(new Money(cents), new DateOnly(2026, 7, 21), Cdi100, Cdi2026.History, Reference)
            .IsSuccess.ShouldBeFalse();

    [Fact]
    public void A_principal_above_the_ceiling_is_refused() =>
        FixedIncomeEngine.Balance(
                new Money(FixedIncomeEngine.MaxPrincipalCents + 1), new DateOnly(2026, 7, 21), Cdi100, Cdi2026.History, Reference)
            .IsSuccess.ShouldBeFalse();

    // Faixa do percentual (D4): de 0,01 % a 500 %, com 4 casas.
    [Theory]
    [InlineData(0)]
    [InlineData(-1_000_000)]
    [InlineData(99)]
    [InlineData(5_000_001)]
    public void A_percent_outside_the_range_is_refused(long tenThousandths) =>
        FixedIncomeEngine.Balance(
                new Money(100_000), new DateOnly(2026, 7, 21), new IndexPercent(tenThousandths), Cdi2026.History, Reference)
            .IsSuccess.ShouldBeFalse();

    [Theory]
    [InlineData(100)]
    [InlineData(5_000_000)]
    public void The_edges_of_the_percent_range_are_accepted(long tenThousandths) =>
        FixedIncomeEngine.Balance(
                new Money(100_000), new DateOnly(2026, 7, 21), new IndexPercent(tenThousandths), Cdi2026.History, Reference)
            .IsSuccess.ShouldBeTrue();

    // ---- Histórico de taxas ----

    [Fact]
    public void A_history_must_be_in_strictly_ascending_date_order()
    {
        RateHistory.Create([new(new DateOnly(2026, 10, 14), 50_000), new(new DateOnly(2026, 10, 13), 50_000)])
            .IsSuccess.ShouldBeFalse();
        RateHistory.Create([new(new DateOnly(2026, 10, 14), 50_000), new(new DateOnly(2026, 10, 14), 50_000)])
            .IsSuccess.ShouldBeFalse();
    }

    // Nunca zero no lugar de taxa ausente, e nada absurdo: ao dia, de 1 × 10⁻⁸ até menos de 1 %.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_000)]
    public void A_rate_outside_the_plausible_range_is_refused(long rateE8) =>
        RateHistory.Create([new(new DateOnly(2026, 10, 14), rateE8)]).IsSuccess.ShouldBeFalse();

    // ---- Grupo ----

    [Fact]
    public void The_group_is_the_sum_of_the_lots_already_rounded()
    {
        var lots = new[]
        {
            Balance(479_731, new DateOnly(2026, 7, 21), Reference),
            Balance(130_000, new DateOnly(2026, 7, 21), Reference),
        };

        var totals = FixedIncomeTotals.Of(lots);

        totals.Principal.ShouldBe(new Money(609_731));
        totals.GrossBalance.ShouldBe(new Money(495_051 + 134_151));
        totals.GrossYield.ShouldBe(new Money(495_051 + 134_151 - 609_731));
        totals.RatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
    }

    [Fact]
    public void An_empty_group_has_zero_totals()
    {
        var totals = FixedIncomeTotals.Of([]);

        totals.Principal.ShouldBe(new Money(0));
        totals.GrossBalance.ShouldBe(new Money(0));
        totals.GrossYield.ShouldBe(new Money(0));
        totals.RatesThrough.ShouldBeNull();
    }
}
