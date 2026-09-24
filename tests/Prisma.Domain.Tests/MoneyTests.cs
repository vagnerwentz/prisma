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
}
