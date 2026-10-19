namespace Prisma.Domain.FixedIncome;

// O ponto fixo do motor (docs/renda-fixa.md, D1): fator em Int128 com 16 casas por passo e 8 no final, saldo em centavos com
// meio centavo para cima. Um lugar só, para o CDI e o pré-fixado arredondarem do mesmo jeito.
internal static class FixedPoint
{
    public const long Scale16 = 10_000_000_000_000_000;

    public const long Scale8 = 100_000_000;

    // O fator acumulado não passa de 1.000×: acima disso o histórico ou a taxa estão errados.
    public static readonly Int128 MaxAccumulated = (Int128)1_000 * Scale16;

    public static long FactorE8(Int128 accumulated) => (long)((accumulated + Scale8 / 2) / (Scale16 / Scale8));

    public static Money Gross(Money principal, long factorE8) =>
        new((long)(((Int128)principal.Cents * factorE8 + Scale8 / 2) / Scale8));
}
