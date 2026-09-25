using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Dashboard;

// Despesas do mês pela categoria raiz, pela SettlementDate (docs/fase-2.md, 2.2). A subcategoria
// soma na categoria pai; sem categoria, CategoryId e Name vêm nulos (a tela escreve "Sem categoria").
public static class ListCategoryTotals
{
    public sealed record Response(Guid? CategoryId, string? Name, string? Icon, string? Color, long AmountCents);

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<IReadOnlyList<Response>>> Execute(string? month, CancellationToken ct)
        {
            var resolved = DashboardMonth.FirstDay(month, clock);
            if (!resolved.IsSuccess) return resolved.Error!;

            var first = resolved.Value;
            var last = first.AddMonths(1).AddDays(-1);

            var byCategory = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Type == TransactionType.Expense && t.SettlementDate >= first && t.SettlementDate <= last)
                .GroupBy(t => t.CategoryId)
                .Select(g => new { CategoryId = g.Key, Cents = g.Sum(t => t.AmountCents) })
                .ToListAsync(ct);

            // Poucas dezenas de categorias por usuário: sobe cada uma até a raiz em memória.
            var categories = await db.Categories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentCategoryId, c.Name, c.Icon, c.Color })
                .ToDictionaryAsync(c => c.Id, ct);

            return byCategory
                .GroupBy(x => x.CategoryId is { } id && categories.TryGetValue(id, out var c) ? c.ParentCategoryId ?? c.Id : (Guid?)null)
                .Select(g =>
                {
                    var root = g.Key is { } rootId ? categories.GetValueOrDefault(rootId) : null;
                    return new Response(root?.Id, root?.Name, root?.Icon, root?.Color, g.Sum(x => x.Cents));
                })
                .OrderByDescending(r => r.AmountCents)
                .ThenBy(r => r.Name)
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/categories", async ([FromQuery] string? month, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(month, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<IReadOnlyList<Response>>(200)
            .ProducesProblem(400);
}
