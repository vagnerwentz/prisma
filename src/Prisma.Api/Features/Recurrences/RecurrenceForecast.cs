using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Accounts;
using Prisma.Domain.Recurrences;
using Prisma.Domain.Statements;

namespace Prisma.Api.Features.Recurrences;

// Carrega o que a previsão das séries precisa e a calcula no domínio (docs/fase-2.md, 2.14, regra 11). Só
// leitura: nada é gravado, e a fatura que ainda não existe não é aberta. cardId: só as séries desse cartão.
public static class RecurrenceForecast
{
    public static async Task<IReadOnlyList<ProjectedOccurrence>> Load(
        AppDbContext db, DateOnly until, CancellationToken ct, Guid? cardId = null)
    {
        var recurrences = await db.Recurrences
            .AsNoTracking()
            .Where(r => (r.EndDate == null || r.EndDate > r.GeneratedThrough) && r.GeneratedThrough < until)
            .Where(r => cardId == null || r.AccountId == cardId)
            .ToListAsync(ct);
        if (recurrences.Count == 0)
            return [];

        var accountIds = recurrences.Select(r => r.AccountId).Distinct().ToList();
        List<Account> accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToListAsync(ct);
        var cardIds = accounts.Where(a => a.Type == AccountType.CreditCard).Select(a => a.Id).ToList();
        List<Statement> statements = cardIds.Count == 0
            ? []
            : await db.Statements.AsNoTracking().Where(s => cardIds.Contains(s.AccountId)).ToListAsync(ct);

        return RecurrenceProjection.Of(recurrences, accounts, statements, until);
    }
}
