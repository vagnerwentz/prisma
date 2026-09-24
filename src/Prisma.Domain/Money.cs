namespace Prisma.Domain;

public readonly record struct Money(long Cents)
{
    // O resto da divisão vai, um centavo por vez, para as primeiras partes:
    // R$ 100,00 em 3x → 3334 + 3333 + 3333. A soma das partes é sempre o total.
    // Em C# o resto herda o sinal do dividendo, então para valores negativos o centavo
    // extra também é negativo: -R$ 100,00 em 3x → -3334 + -3333 + -3333.
    public IReadOnlyList<Money> SplitInto(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var baseCents = Cents / count;
        var remainder = Cents % count;
        var extraCents = Math.Sign(remainder);
        var partsWithExtra = Math.Abs(remainder);

        var parts = new Money[count];
        for (var i = 0; i < count; i++)
            parts[i] = new Money(i < partsWithExtra ? baseCents + extraCents : baseCents);

        return parts;
    }
}
