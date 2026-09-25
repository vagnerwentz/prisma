using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Por que o gasto mudou (docs/fase-2.md, 2.7). O banco traz as despesas e os estornos dos dois meses,
// pela SettlementDate; o domínio decompõe a diferença. A resposta é estruturada: a tela (ou, um dia,
// uma IA) só redige os motivos, nunca os calcula.
public static class GetSpendingVariation
{
    public sealed record Entry(Guid TransactionId, string Description, long AmountCents);

    public sealed record Purchase(Guid PurchaseId, string Description, long AmountCents);

    // Categoria: CategoryId e Name nulos são "Sem categoria". Largest só na categoria que subiu;
    // Started e Ended só nas parcelas.
    public sealed record Reason(
        VariationReasonKind Kind, Guid? CategoryId, string? Name, string? Icon, string? Color, long ChangeCents,
        Entry? Largest, IReadOnlyList<Purchase> Started, IReadOnlyList<Purchase> Ended);

    public sealed record Response(
        string Month, string PreviousMonth, long ExpenseCents, long PreviousExpenseCents, long ChangeCents,
        IReadOnlyList<Reason> Reasons);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<Response>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var first = resolved.Value;
            var previousFirst = first.AddMonths(-1);
            var last = first.AddMonths(1).AddDays(-1);

            var rows = await db.Transactions
                .AsNoTracking()
                .Where(t => (t.Type == TransactionType.Expense || t.Type == TransactionType.Refund)
                            && t.SettlementDate >= previousFirst && t.SettlementDate <= last)
                .Select(t => new
                {
                    t.Id, t.Type, t.CategoryId, t.Description, t.AmountCents, t.SettlementDate, t.InstallmentPurchaseId,
                    t.InstallmentNumber,
                    InstallmentCount = db.InstallmentPurchases
                        .Where(p => p.Id == t.InstallmentPurchaseId)
                        .Select(p => (int?)p.InstallmentCount)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            // Poucas dezenas de categorias por usuário: sobe cada uma até a raiz em memória.
            var categories = await db.Categories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentCategoryId, c.Name, c.Icon, c.Color })
                .ToDictionaryAsync(c => c.Id, ct);

            var entries = rows.Select(r =>
            {
                var root = r.CategoryId is { } id && categories.TryGetValue(id, out var c)
                    ? categories.GetValueOrDefault(c.ParentCategoryId ?? c.Id)
                    : null;
                return (r.SettlementDate, Entry: new SpendingEntry(
                    r.Id, r.Type, root?.Id, root?.Name, r.Description, r.AmountCents, r.InstallmentPurchaseId,
                    r.InstallmentNumber, r.InstallmentCount));
            }).ToList();

            var variation = SpendingVariation.Of(
                entries.Where(e => e.SettlementDate >= first).Select(e => e.Entry),
                entries.Where(e => e.SettlementDate < first).Select(e => e.Entry));

            return new Response(
                DashboardMonth.Format(first), DashboardMonth.Format(previousFirst), variation.ExpenseCents,
                variation.PreviousExpenseCents, variation.ChangeCents,
                variation.Reasons.Select(r =>
                {
                    var category = r.CategoryId is { } id ? categories.GetValueOrDefault(id) : null;
                    return new Reason(
                        r.Kind, r.CategoryId, category?.Name, category?.Icon, category?.Color, r.ChangeCents,
                        r.Largest is { } l ? new Entry(l.TransactionId, l.Description, l.AmountCents) : null,
                        r.Started.Select(p => new Purchase(p.PurchaseId, p.Description, p.AmountCents)).ToList(),
                        r.Ended.Select(p => new Purchase(p.PurchaseId, p.Description, p.AmountCents)).ToList());
                }).ToList());
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/variation", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<Response>(200)
            .ProducesProblem(400);
}
