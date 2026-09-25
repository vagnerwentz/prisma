using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Dashboard;

namespace Prisma.Api.Features.Dashboard;

// As somas agrupadas que alimentam as regras do resumo (docs/fase-2.md, 2.1), pela SettlementDate.
// Resumo, comparativo, parcelas herdadas e já comprometido usam as mesmas consultas: o "Saiu" é o
// mesmo número em todos os blocos.
public static class DashboardEntries
{
    public static Task<List<SummaryEntry>> InPeriod(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct) =>
        db.Transactions
            .AsNoTracking()
            .Where(t => t.SettlementDate >= from && t.SettlementDate <= to)
            .Join(db.Accounts, t => t.AccountId, a => a.Id, (t, a) => new { t.Type, t.TransferDirection, AccountType = a.Type, t.AmountCents })
            .GroupBy(x => new { x.Type, x.TransferDirection, x.AccountType })
            .Select(g => new SummaryEntry(g.Key.Type, g.Key.TransferDirection, g.Key.AccountType, g.Sum(x => x.AmountCents)))
            .ToListAsync(ct);

    public static async Task<List<MonthlyEntry>> ByMonth(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.SettlementDate >= from && t.SettlementDate <= to)
            .Join(db.Accounts, t => t.AccountId, a => a.Id, (t, a) => new
            {
                t.SettlementDate.Year, t.SettlementDate.Month, t.Type, t.TransferDirection, AccountType = a.Type, t.AmountCents,
            })
            .GroupBy(x => new { x.Year, x.Month, x.Type, x.TransferDirection, x.AccountType })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Type, g.Key.TransferDirection, g.Key.AccountType, Cents = g.Sum(x => x.AmountCents) })
            .ToListAsync(ct);

        return rows
            .Select(r => new MonthlyEntry(new DateOnly(r.Year, r.Month, 1), new SummaryEntry(r.Type, r.TransferDirection, r.AccountType, r.Cents)))
            .ToList();
    }
}
