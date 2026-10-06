namespace Prisma.Domain.Investments;

// O que o ativo pagou (docs/investimentos.md, etapa 5b). Na tela: dividendo, JCP, rendimento.
public enum PayoutKind
{
    Dividend,

    // Juros sobre capital próprio. O valor lançado é o líquido (15% de IR já retido).
    InterestOnEquity,

    // Rendimento de FII e de outros fundos listados.
    FundIncome,
}
