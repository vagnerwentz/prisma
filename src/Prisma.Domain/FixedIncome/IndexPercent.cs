namespace Prisma.Domain.FixedIncome;

// Percentual contratado do índice (CDI), em décimos de milésimo de ponto percentual: 100 % = 1.000.000, 110,5 % = 1.105.000.
// Quatro casas, como a B3 informa o percentual destacado para a remuneração (docs/renda-fixa.md, seção 5.1). Inteiro,
// nunca decimal: a conta do fator diário fica exata em inteiro.
//
// Faixa aceita (docs/renda-fixa.md, D4): de 0,01 % a 500 %.
public readonly record struct IndexPercent(long TenThousandths)
{
    public const long Min = 100;

    public const long Max = 5_000_000;

    public bool IsInRange => TenThousandths is >= Min and <= Max;
}
