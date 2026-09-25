using System.Globalization;

namespace Prisma.Domain.Dashboard;

// Uma fatura de cartão com o total das compras (o pagamento não conta).
public readonly record struct CardStatement(
    Guid AccountId, string CardName, Guid StatementId, DateOnly DueDate, bool IsPaid, long TotalCents);

// Próximas faturas (docs/fase-2.md, 2.4): de cada cartão, a primeira não paga que vence hoje ou
// depois, com total maior que zero. A mais próxima primeiro; empate no vencimento, pelo nome.
public sealed record UpcomingStatements(IReadOnlyList<CardStatement> Statements, long TotalCents)
{
    private static readonly StringComparer PortugueseOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    public static UpcomingStatements Of(DateOnly today, IEnumerable<CardStatement> statements)
    {
        var next = statements
            .Where(s => !s.IsPaid && s.DueDate >= today && s.TotalCents > 0)
            .GroupBy(s => s.AccountId)
            .Select(card => card.MinBy(s => s.DueDate))
            .OrderBy(s => s.DueDate)
            .ThenBy(s => s.CardName, PortugueseOrder)
            .ThenBy(s => s.AccountId)
            .ToList();

        return new UpcomingStatements(next, next.Sum(s => s.TotalCents));
    }
}
