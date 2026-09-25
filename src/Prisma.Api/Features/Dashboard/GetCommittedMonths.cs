using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Já comprometido (docs/fase-2.md, 2.6): os 6 meses seguintes ao de hoje, olhando para hoje e não
// para o mês do Resumo, e o mês da última parcela lançada.
public static class GetCommittedMonths
{
    public sealed record Item(string Month, long ExpenseCents);

    public sealed record Response(IReadOnlyList<Item> Months, string? LastInstallmentMonth);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Response> Execute(CancellationToken ct)
        {
            var today = clock.Today;
            var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            var end = nextMonth.AddMonths(CommittedMonths.Horizon).AddDays(-1);

            var entries = await DashboardEntries.ByMonth(db, nextMonth, end, ct);
            var lastInstallmentDue = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.InstallmentPurchaseId != null && t.SettlementDate >= nextMonth)
                .MaxAsync(t => (DateOnly?)t.SettlementDate, ct);

            var committed = CommittedMonths.Of(today, entries, lastInstallmentDue);
            return new Response(
                committed.Months.Select(m => new Item(DashboardMonth.Format(m.Month), m.ExpenseCents)).ToList(),
                committed.LastInstallmentMonth is { } last ? DashboardMonth.Format(last) : null);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/committed", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<Response>(200);
}
