using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

// O total da fatura soma só as compras: a entrada do pagamento (Transfer) fica ligada à fatura,
// mas não é gasto (docs/fase-1.md, 2.3).
public static class StatementTotals
{
    public static Task<long> Of(AppDbContext db, Guid statementId, CancellationToken ct) =>
        db.Transactions
            .Where(t => t.StatementId == statementId && t.Type != TransactionType.Transfer)
            .SumAsync(t => t.AmountCents, ct);
}
