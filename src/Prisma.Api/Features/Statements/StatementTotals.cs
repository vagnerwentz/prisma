using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Statements;

// O total da fatura é compras menos estornos: a entrada do pagamento (Transfer) fica ligada à
// fatura, mas não é gasto (docs/fase-1.md, 2.3). Negativo é saldo a favor (docs/fase-2.md, 2.5).
// ListStatements, ListUpcomingStatements e ListAccountBalances repetem a mesma soma dentro das
// consultas deles.
public static class StatementTotals
{
    public static Task<long> Of(AppDbContext db, Guid statementId, CancellationToken ct) =>
        db.Transactions
            .Where(t => t.StatementId == statementId && t.Type != TransactionType.Transfer)
            .SumAsync(t => t.Type == TransactionType.Refund ? -t.AmountCents : t.AmountCents, ct);
}
