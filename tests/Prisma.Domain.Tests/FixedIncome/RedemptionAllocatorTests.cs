using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// A divisão do resgate em lote (docs/renda-fixa-resgate.md, 15.3 e 15.5). Os vetores vêm do caso do dono (extrato de 01/09/2025: bruto
// R$ 5.395,41, líquido R$ 5.360,02, sobre os saldos de três aportes de CDB a 100 % do CDI), calculados à mão na especificação, não do código.
public sealed class RedemptionAllocatorTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    // Saldos brutos em 01/09/2025: 12/05 (1.115,18 + 49,35), 18/06 (3.167,48 + 92,07) e 19/07 (955,43 + 15,93).
    private static readonly AllocationCandidate OldestA = new(A, new DateOnly(2025, 5, 12), 116_453);
    private static readonly AllocationCandidate MiddleB = new(B, new DateOnly(2025, 6, 18), 325_955);
    private static readonly AllocationCandidate NewestC = new(C, new DateOnly(2025, 7, 19), 97_136);

    // ---------- divisão do bruto ----------

    [Fact]
    public void Owners_case_takes_the_oldest_in_full_and_the_newest_in_part()
    {
        // O extrato deu R$ 5.395,41; o app calcula R$ 5.395,44: o último aporte sai 3 centavos abaixo do saldo.
        var parts = RedemptionAllocator.Allocate([OldestA, MiddleB, NewestC], 539_541);

        parts.IsSuccess.ShouldBeTrue();
        parts.Value.ShouldBe([new AllocationPart(A, 116_453), new AllocationPart(B, 325_955), new AllocationPart(C, 97_133)]);
    }

    [Fact]
    public void Order_is_by_deposit_date_whatever_the_input_order()
    {
        var parts = RedemptionAllocator.Allocate([NewestC, OldestA, MiddleB], 200_000);

        parts.Value.ShouldBe([new AllocationPart(A, 116_453), new AllocationPart(B, 83_547)]);
    }

    [Fact]
    public void Same_day_deposits_keep_the_input_order()
    {
        var first = new AllocationCandidate(A, new DateOnly(2025, 6, 18), 1_000);
        var second = new AllocationCandidate(B, new DateOnly(2025, 6, 18), 1_000);

        RedemptionAllocator.Allocate([first, second], 1_500).Value
            .ShouldBe([new AllocationPart(A, 1_000), new AllocationPart(B, 500)]);
        RedemptionAllocator.Allocate([second, first], 1_500).Value
            .ShouldBe([new AllocationPart(B, 1_000), new AllocationPart(A, 500)]);
    }

    [Fact]
    public void Deposits_without_balance_are_skipped()
    {
        var empty = new AllocationCandidate(B, new DateOnly(2025, 5, 20), 0);

        RedemptionAllocator.Allocate([OldestA, empty, NewestC], 116_453 + 10).Value
            .ShouldBe([new AllocationPart(A, 116_453), new AllocationPart(C, 10)]);
    }

    [Fact]
    public void A_value_that_ends_exactly_on_a_balance_leaves_no_empty_part()
    {
        RedemptionAllocator.Allocate([OldestA, MiddleB], 116_453).Value.ShouldBe([new AllocationPart(A, 116_453)]);
    }

    [Fact]
    public void Redeeming_everything_takes_every_balance()
    {
        var candidates = new[] { OldestA, MiddleB, NewestC };
        var total = RedemptionAllocator.TotalBalance(candidates);

        total.ShouldBe(539_544);
        RedemptionAllocator.Allocate(candidates, total).Value
            .ShouldBe([new AllocationPart(A, 116_453), new AllocationPart(B, 325_955), new AllocationPart(C, 97_136)]);
    }

    [Fact]
    public void A_value_above_the_sum_of_balances_is_refused()
    {
        var result = RedemptionAllocator.Allocate([OldestA, MiddleB, NewestC], 539_545);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldContain("R$ 5.395,44");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_value_below_one_cent_is_refused(long gross)
    {
        RedemptionAllocator.Allocate([OldestA], gross).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void No_deposit_with_balance_is_refused()
    {
        RedemptionAllocator.Allocate([], 100).IsSuccess.ShouldBeFalse();
        RedemptionAllocator.Allocate([new AllocationCandidate(A, new DateOnly(2025, 5, 12), 0)], 100).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Parts_add_up_to_the_value_and_only_the_last_one_is_partial()
    {
        Gen.Select(Gen.Long[1, 5_000_000_00].List[1, 8], Gen.Double[0.0, 1.0])
            .Sample((balances, share) =>
            {
                var candidates = balances
                    .Select((cents, i) => new AllocationCandidate(Guid.NewGuid(), new DateOnly(2025, 1, 1).AddDays(i), cents))
                    .ToList();
                var total = RedemptionAllocator.TotalBalance(candidates);
                var gross = Math.Clamp((long)(total * share), 1, total);

                var parts = RedemptionAllocator.Allocate(candidates.OrderBy(_ => Guid.NewGuid()).ToList(), gross);

                parts.IsSuccess.ShouldBeTrue();
                parts.Value.Sum(p => p.GrossCents).ShouldBe(gross);
                foreach (var part in parts.Value)
                {
                    var balance = candidates.Single(c => c.DepositId == part.DepositId).BalanceCents;
                    part.GrossCents.ShouldBeInRange(1, balance);
                }

                // Do mais antigo ao mais novo, todos saem por inteiro, menos o último da lista.
                foreach (var part in parts.Value.SkipLast(1))
                    part.GrossCents.ShouldBe(candidates.Single(c => c.DepositId == part.DepositId).BalanceCents);
            }, iter: 2_000);
    }

    // ---------- repartição do líquido do extrato ----------

    // Os três pedaços do caso do dono, com o imposto estimado de cada um (11,10, 20,72 e 3,58) e o rendimento que saiu.
    private static readonly NetLine LineA = new(A, 116_453, 4_935, 1_110);
    private static readonly NetLine LineB = new(B, 325_955, 9_207, 2_072);
    private static readonly NetLine LineC = new(C, 97_133, 1_590, 358);

    [Fact]
    public void Owners_net_is_split_by_the_estimated_tax_and_adds_up_to_the_statement()
    {
        // Bruto 5.395,41, líquido 5.360,02: imposto de R$ 35,39 (o app estimou R$ 35,40). Pisos: 11,09 + 20,71 + 3,57 = 35,37; os 2 centavos
        // que faltam vão um a um da última linha para trás.
        var nets = RedemptionAllocator.DistributeNet([LineA, LineB, LineC], 536_002);

        nets.IsSuccess.ShouldBeTrue();
        nets.Value.ShouldBe([new NetPart(A, 115_344), new NetPart(B, 323_883), new NetPart(C, 96_775)]);
        nets.Value.Sum(n => n.NetCents).ShouldBe(536_002);
    }

    [Fact]
    public void A_net_equal_to_the_estimate_changes_nothing()
    {
        var nets = RedemptionAllocator.DistributeNet([LineA, LineB, LineC], 539_541 - 3_540);

        nets.Value.ShouldBe([new NetPart(A, 116_453 - 1_110), new NetPart(B, 325_955 - 2_072), new NetPart(C, 97_133 - 358)]);
    }

    [Fact]
    public void A_net_equal_to_the_gross_means_no_tax()
    {
        RedemptionAllocator.DistributeNet([LineA, LineB], 116_453 + 325_955).Value
            .ShouldBe([new NetPart(A, 116_453), new NetPart(B, 325_955)]);
    }

    [Fact]
    public void Without_estimated_tax_the_split_follows_the_yield()
    {
        // IR zero estimado (LCI/LCA) e o extrato cobrou 100: rendimentos 300 e 100 levam 75 e 25.
        var a = new NetLine(A, 10_000, 300, 0);
        var b = new NetLine(B, 10_000, 100, 0);

        RedemptionAllocator.DistributeNet([a, b], 20_000 - 100).Value.ShouldBe([new NetPart(A, 10_000 - 75), new NetPart(B, 10_000 - 25)]);
    }

    [Fact]
    public void A_leftover_cent_skips_a_line_that_has_no_yield_left()
    {
        // Imposto de 1 centavo entre duas linhas de estimativa igual: os pisos dão 0 e 0, e o centavo sobra. A última linha não tem
        // rendimento (resgate só do principal), então o centavo vai para a anterior, e nunca para onde o imposto passaria do rendimento.
        var a = new NetLine(A, 1_000, 10, 1);
        var b = new NetLine(B, 1_000, 10, 1);
        var c = new NetLine(C, 1_000, 0, 0);

        RedemptionAllocator.DistributeNet([a, b, c], 3_000 - 1).Value
            .ShouldBe([new NetPart(A, 1_000), new NetPart(B, 999), new NetPart(C, 1_000)]);
    }

    [Fact]
    public void A_net_above_the_gross_is_refused()
    {
        RedemptionAllocator.DistributeNet([LineA, LineB], 116_453 + 325_955 + 1).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void A_tax_above_the_total_yield_is_refused()
    {
        var gross = 116_453 + 325_955;
        var result = RedemptionAllocator.DistributeNet([LineA, LineB], gross - (4_935 + 9_207) - 1);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void A_split_that_would_push_a_line_over_its_yield_is_refused()
    {
        // A estimou 100 de imposto sobre rendimento 100; B estimou 1 sobre 1.000. Imposto total de 500 (> estimativa) daria 495 para A.
        var a = new NetLine(A, 5_000, 100, 100);
        var b = new NetLine(B, 5_000, 1_000, 1);

        RedemptionAllocator.DistributeNet([a, b], 10_000 - 500).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Every_line_keeps_its_tax_between_zero_and_its_yield()
    {
        Gen.Select(Gen.Long[1, 1_000_00].List[1, 6], Gen.Double[0.0, 1.0], Gen.Double[0.0, 1.0].List[1, 6])
            .Sample((yields, share, estimates) =>
            {
                var lines = yields
                    .Select((y, i) => new NetLine(Guid.NewGuid(), y + 10_000, y, (long)(y * estimates[i % estimates.Count])))
                    .ToList();
                var estimated = lines.Sum(l => l.EstimatedTaxCents);
                // Até a estimativa: o piso de cada linha não passa da estimativa dela, que não passa do rendimento.
                var tax = (long)(estimated * share);
                var gross = lines.Sum(l => l.GrossCents);

                var nets = RedemptionAllocator.DistributeNet(lines, gross - tax);

                nets.IsSuccess.ShouldBeTrue();
                nets.Value.Sum(n => n.NetCents).ShouldBe(gross - tax);
                for (var i = 0; i < lines.Count; i++)
                {
                    var lineTax = lines[i].GrossCents - nets.Value[i].NetCents;
                    lineTax.ShouldBeInRange(0, lines[i].YieldCents);
                    nets.Value[i].DepositId.ShouldBe(lines[i].DepositId);
                }
            }, iter: 2_000);
    }
}
