using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Dashboard;

namespace Prisma.Api.Features.Dashboard;

// Receitas, despesas, sobra e investido do mês, pela SettlementDate (docs/fase-2.md, 2.1).
// O banco soma agrupado; o domínio decide o que é receita, despesa e investido.
// EstimatedExpenseCents: quanto do "Saiu" ainda é estimativa de débito automático, a conferir (2.15, regra 8).
public static class GetMonthlySummary
{
    public sealed record Response(
        string Month, long IncomeCents, long ExpenseCents, long CardExpenseCents, long LeftoverCents, long InvestedCents,
        long EstimatedExpenseCents = 0);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<Response>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var first = resolved.Value;
            var last = first.AddMonths(1).AddDays(-1);

            var entries = await DashboardEntries.InPeriod(db, first, last, ct);
            var summary = MonthlySummary.Of(entries);
            // Só despesa fica a conferir (o débito automático é sempre despesa).
            var estimated = await db.Transactions
                .AsNoTracking()
                .Where(t => t.AmountEstimated && t.SettlementDate >= first && t.SettlementDate <= last)
                .SumAsync(t => t.AmountCents, ct);
            return new Response(
                DashboardMonth.Format(first), summary.IncomeCents, summary.ExpenseCents, summary.CardExpenseCents,
                summary.LeftoverCents, summary.InvestedCents, estimated);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/summary", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<Response>(200)
            .ProducesProblem(400);
}
