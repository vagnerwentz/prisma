using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Parcelas de compras anteriores no mês (docs/fase-2.md, 2.6). O banco traz o "Saiu" do mês e as
// parcelas que vencem nele; o domínio decide o que é herdado e a ordem.
public static class GetInheritedInstallments
{
    public sealed record Item(
        Guid TransactionId, Guid PurchaseId, string Description, Guid? CategoryId, Guid AccountId,
        int InstallmentNumber, int InstallmentCount, long AmountCents, DateOnly PurchaseDate);

    public sealed record Response(
        string Month, long ExpenseCents, long InheritedCents, long? DecidedInMonthCents, IReadOnlyList<Item> Items);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<Response>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var first = resolved.Value;
            var last = first.AddMonths(1).AddDays(-1);

            var expense = MonthlySummary.Of(await DashboardEntries.InPeriod(db, first, last, ct)).ExpenseCents;

            var charges = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.InstallmentPurchaseId != null && t.InstallmentNumber != null
                    && t.SettlementDate >= first && t.SettlementDate <= last)
                .Join(db.InstallmentPurchases, t => t.InstallmentPurchaseId, p => p.Id, (t, p) => new InstallmentCharge(
                    t.Id, p.Id, t.Description, t.CategoryId, t.AccountId, t.InstallmentNumber!.Value, p.InstallmentCount,
                    t.AmountCents, t.PurchaseDate))
                .ToListAsync(ct);

            var inherited = InheritedInstallments.Of(expense, charges);
            return new Response(
                DashboardMonth.Format(first), expense, inherited.InheritedCents, inherited.DecidedInMonthCents,
                inherited.Charges.Select(c => new Item(
                    c.TransactionId, c.PurchaseId, c.Description, c.CategoryId, c.AccountId, c.Number, c.Count, c.AmountCents,
                    c.PurchaseDate)).ToList());
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/inherited", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<Response>(200)
            .ProducesProblem(400);
}
