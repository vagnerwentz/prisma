namespace Prisma.Domain.Market;

// Tipo de um ativo listado na bolsa, independente do fornecedor dos dados (docs/investimentos.md, seção 3).
// Cada fornecedor traduz o seu vocabulário para cá; o resto do app só conhece este.
public enum AssetKind
{
    // Ações
    Stock,
    Unit,

    // Fundos listados
    Fii,
    Etf,
    FiInfra,
    FiAgro,
    Fip,
    Fidc,
    OtherFund,

    Bdr,

    // O fornecedor mandou um tipo que ainda não conhecemos: o ativo entra, e o log avisa.
    Unknown,
}
