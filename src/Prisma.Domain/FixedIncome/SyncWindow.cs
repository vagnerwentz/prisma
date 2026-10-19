namespace Prisma.Domain.FixedIncome;

// O período que a pessoa pede para reprocessar na tela de admin (docs/admin-mercado.md, 4 e 6). O teto existe para nada
// livre ir ao Banco Central: no máximo 10 anos, nunca antes do início do histórico configurado e nunca no futuro.
public static class SyncWindow
{
    public const int MaxYears = 10;

    // Devolve a mensagem (pt-BR) do primeiro problema, ou nulo se o período vale.
    public static string? Validate(DateOnly from, DateOnly to, DateOnly today, DateOnly earliest)
    {
        if (from > to)
            return "A data inicial não pode ser depois da final.";
        if (to > today)
            return "A data final não pode estar no futuro.";
        if (from < earliest)
            return $"A data inicial não pode ser antes de {earliest:dd/MM/yyyy}, o início do histórico.";
        if (to > from.AddYears(MaxYears))
            return $"O período pode ter no máximo {MaxYears} anos.";
        return null;
    }
}
