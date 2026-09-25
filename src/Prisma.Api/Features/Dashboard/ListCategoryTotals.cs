using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Dashboard;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Despesas do mês pela categoria raiz, pela SettlementDate (docs/fase-2.md, 2.2). A subcategoria
// soma na categoria pai; sem categoria, CategoryId e Name vêm nulos (a tela escreve "Sem categoria").
// Estornos abatem a categoria deles; a que fica zero ou negativa some, e HiddenRefundCents diz
// quanto de estorno sumiu com elas (docs/fase-2.md, 2.5, regra 12).
public static class ListCategoryTotals
{
    public sealed record Item(Guid? CategoryId, string? Name, string? Icon, string? Color, long AmountCents);

    public sealed record Response(IReadOnlyList<Item> Categories, long HiddenRefundCents);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<Response>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var first = resolved.Value;
            var last = first.AddMonths(1).AddDays(-1);

            var byCategory = await db.Transactions
                .AsNoTracking()
                .Where(t => (t.Type == TransactionType.Expense || t.Type == TransactionType.Refund)
                            && t.SettlementDate >= first && t.SettlementDate <= last)
                .GroupBy(t => new { t.CategoryId, t.Type })
                .Select(g => new { g.Key.CategoryId, g.Key.Type, Cents = g.Sum(t => t.AmountCents) })
                .ToListAsync(ct);

            // Poucas dezenas de categorias por usuário: sobe cada uma até a raiz em memória.
            var categories = await db.Categories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentCategoryId, c.Name, c.Icon, c.Color })
                .ToDictionaryAsync(c => c.Id, ct);

            Guid? RootOf(Guid? id) =>
                id is { } categoryId && categories.TryGetValue(categoryId, out var c) ? c.ParentCategoryId ?? c.Id : null;

            var totals = CategoryTotals.Of(byCategory.Select(x => new CategoryAmount(RootOf(x.CategoryId), x.Type, x.Cents)));

            var items = totals.Shown
                .Select(net =>
                {
                    var root = net.RootId is { } rootId ? categories.GetValueOrDefault(rootId) : null;
                    return new Item(root?.Id, root?.Name, root?.Icon, root?.Color, net.AmountCents);
                })
                .OrderByDescending(r => r.AmountCents)
                .ThenBy(r => r.Name)
                .ToList();
            return new Response(items, totals.HiddenRefundCents);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/categories", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<Response>(200)
            .ProducesProblem(400);
}
