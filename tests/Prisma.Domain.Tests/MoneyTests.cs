using CsCheck;
using Shouldly;

namespace Prisma.Domain.Tests;

public sealed class MoneyTests
{
    // Faixas da especificação (docs/fase-1.md, seção 4).
    private static readonly Gen<(long Total, int Count)> TotalAndCount =
        Gen.Select(Gen.Long[0, 10_000_000], Gen.Int[1, 360]);

    [Fact]
    public void SplitInto_parts_always_sum_to_the_total() =>
        TotalAndCount.Sample((total, count) =>
            new Money(total).SplitInto(count).Sum(part => part.Cents) == total);

    // CLAUDE.md, regra 2: a soma é exata "para qualquer entrada", inclusive valores negativos.
    [Fact]
    public void SplitInto_parts_sum_to_the_total_for_any_long() =>
        Gen.Select(Gen.Long, Gen.Int[1, 360]).Sample((total, count) =>
            new Money(total).SplitInto(count).Sum(part => part.Cents) == total);

    [Fact]
    public void SplitInto_returns_exactly_n_parts() =>
        TotalAndCount.Sample((total, count) =>
            new Money(total).SplitInto(count).Count == count);

    // "Distribui o resto nas primeiras parcelas": as partes diferem em no máximo um centavo
    // e as maiores vêm primeiro.
    [Fact]
    public void SplitInto_puts_the_remainder_in_the_first_parts() =>
        TotalAndCount.Sample((total, count) =>
        {
            var cents = new Money(total).SplitInto(count).Select(part => part.Cents).ToList();
            var baseCents = total / count;
            return cents.All(c => c == baseCents || c == baseCents + 1)
                && cents.Zip(cents.Skip(1)).All(pair => pair.First >= pair.Second);
        });

    [Theory]
    [InlineData(10000, 3, new long[] { 3334, 3333, 3333 })]
    [InlineData(1, 2, new long[] { 1, 0 })]
    [InlineData(0, 5, new long[] { 0, 0, 0, 0, 0 })]
    [InlineData(12990, 1, new long[] { 12990 })]
    [InlineData(-10000, 3, new long[] { -3334, -3333, -3333 })]
    public void SplitInto_matches_the_specified_examples(long total, int count, long[] expected) =>
        new Money(total).SplitInto(count).Select(part => part.Cents).ShouldBe(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SplitInto_rejects_fewer_than_one_part(int count) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(10000).SplitInto(count));

    // Rateio proporcional (docs/investimentos.md, "I.7a Cálculo"): as taxas da nota se dividem pelo valor de cada
    // operação. Pesos até R$ 10 milhões por operação, total até R$ 10 milhões.
    private static readonly Gen<(long Total, long[] Weights)> TotalAndWeights =
        Gen.Select(Gen.Long[0, 1_000_000_000], Gen.Long[0, 1_000_000_000].Array[1, 30])
            .Where((_, weights) => weights.Sum() > 0);

    [Fact]
    public void Allocate_parts_always_sum_to_the_total() =>
        TotalAndWeights.Sample((total, weights) =>
            new Money(total).Allocate(weights).Sum(part => part.Cents) == total);

    // Cada parte fica a menos de um centavo da parte exata (total × peso ÷ soma dos pesos).
    [Fact]
    public void Allocate_parts_are_within_a_cent_of_the_exact_share() =>
        TotalAndWeights.Sample((total, weights) =>
        {
            var parts = new Money(total).Allocate(weights);
            Int128 sum = weights.Sum();
            return parts.Select((part, i) => Int128.Abs((Int128)part.Cents * sum - (Int128)total * weights[i]))
                .All(distance => distance < sum);
        });

    // Pesos iguais: o mesmo resultado da divisão das parcelas.
    [Fact]
    public void Allocate_with_equal_weights_matches_SplitInto() =>
        Gen.Select(Gen.Long[0, 10_000_000], Gen.Int[1, 360], Gen.Long[1, 1_000_000]).Sample((total, count, weight) =>
            new Money(total).Allocate(Enumerable.Repeat(weight, count).ToArray())
                .SequenceEqual(new Money(total).SplitInto(count)));

    [Theory]
    [InlineData(1000, new long[] { 800000, 200000 }, new long[] { 800, 200 })]
    [InlineData(11, new long[] { 10000, 20000, 70000 }, new long[] { 1, 2, 8 })]  // maior resto, não a primeira
    [InlineData(100, new long[] { 100000, 100000, 100000 }, new long[] { 34, 33, 33 })] // empate: a que vem antes
    [InlineData(100, new long[] { 200000, 300000 }, new long[] { 40, 60 })]
    [InlineData(0, new long[] { 5, 7 }, new long[] { 0, 0 })]
    [InlineData(10, new long[] { 0, 3 }, new long[] { 0, 10 })]
    public void Allocate_matches_the_specified_examples(long total, long[] weights, long[] expected) =>
        new Money(total).Allocate(weights).Select(part => part.Cents).ShouldBe(expected);

    [Fact]
    public void Allocate_rejects_invalid_input()
    {
        Should.Throw<ArgumentException>(() => new Money(100).Allocate([]));
        Should.Throw<ArgumentException>(() => new Money(100).Allocate([0, 0]));
        Should.Throw<ArgumentException>(() => new Money(100).Allocate([5, -1]));
        Should.Throw<ArgumentException>(() => new Money(-100).Allocate([1, 1]));
    }
}
