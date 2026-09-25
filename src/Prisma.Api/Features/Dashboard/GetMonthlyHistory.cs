using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Dashboard;

namespace Prisma.Api.Features.Dashboard;

// Os 6 meses que terminam no escolhido, mais antigo primeiro (docs/fase-2.md, 2.3). Uma consulta
// agrupada por mês da SettlementDate; o domínio monta os meses, inclusive os vazios.
public static class GetMonthlyHistory
{
    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<IReadOnlyList<GetMonthlySummary.Response>>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var lastMonth = resolved.Value;
            var from = MonthlyHistory.FirstMonth(lastMonth);
            var to = lastMonth.AddMonths(1).AddDays(-1);

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

            var entries = rows.Select(r => new MonthlyEntry(
                new DateOnly(r.Year, r.Month, 1), new SummaryEntry(r.Type, r.TransferDirection, r.AccountType, r.Cents)));

            return MonthlyHistory.Of(lastMonth, entries)
                .Select(m => new GetMonthlySummary.Response(
                    DashboardMonth.Format(m.Month), m.Summary.IncomeCents, m.Summary.ExpenseCents, m.Summary.CardExpenseCents,
                    m.Summary.LeftoverCents, m.Summary.InvestedCents))
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/history", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<IReadOnlyList<GetMonthlySummary.Response>>(200)
            .ProducesProblem(400);
}
