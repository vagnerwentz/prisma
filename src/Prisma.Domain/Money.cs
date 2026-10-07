namespace Prisma.Domain;

public readonly record struct Money(long Cents)
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    // "R$ 1.234,56", para mensagens ao usuário. Só inteiros: centavos nunca passam por decimal.
    public override string ToString()
    {
        var abs = Math.Abs(Cents);
        var text = $"R$ {(abs / 100).ToString("N0", PtBr)},{abs % 100:00}";
        return Cents < 0 ? "-" + text : text;
    }

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

    // Rateio proporcional aos pesos: R$ 0,11 por R$ 100, R$ 200 e R$ 700 → 1 + 2 + 8. Cada parte começa no piso da
    // parte exata, e os centavos que sobram vão, um a um, para as maiores sobras (empate: a parte que vem antes).
    // Pesos iguais dão o mesmo que SplitInto. A soma das partes é sempre o total; conta em Int128 para o
    // produto valor × peso não estourar.
    public IReadOnlyList<Money> Allocate(IReadOnlyList<long> weights)
    {
        if (Cents < 0)
            throw new ArgumentException("O rateio é só de valor não negativo.", nameof(weights));
        if (weights.Count == 0 || weights.Any(w => w < 0))
            throw new ArgumentException("Os pesos não podem ser vazios nem negativos.", nameof(weights));

        Int128 totalWeight = 0;
        foreach (var weight in weights)
            totalWeight += weight;
        if (totalWeight == 0)
            throw new ArgumentException("Ao menos um peso precisa ser maior que zero.", nameof(weights));

        var parts = new long[weights.Count];
        var remainders = new Int128[weights.Count];
        var leftover = Cents;
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = (Int128)Cents * weights[i];
            parts[i] = (long)(exact / totalWeight);
            remainders[i] = exact % totalWeight;
            leftover -= parts[i];
        }

        // Sobram menos centavos que partes.
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => remainders[i]).Take((int)leftover))
            parts[i]++;

        return parts.Select(cents => new Money(cents)).ToArray();
    }
}
