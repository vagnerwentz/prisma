using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Categories;

public static class ListCategories
{
    public sealed record CategoryNode(
        Guid Id,
        string Name,
        TransactionType Type,
        string? Icon,
        string? Color,
        IReadOnlyList<CategoryResponse> Subcategories);

    // Ordem alfabética em português ("Água" antes de "Aluguel"), independente da collation do banco.
    private static readonly StringComparer PortugueseOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    public sealed class Handler(AppDbContext db, CategoryCatalog catalog)
    {
        public async Task<IReadOnlyList<CategoryNode>> Execute(CancellationToken ct)
        {
            // Categorias novas do catálogo chegam aqui, uma vez por versão (docs/fase-2.md, 2.11).
            await catalog.EnsureCurrent(ct);

            var all = await db.Categories
                .AsNoTracking()
                .Select(CategoryResponse.Projection)
                .ToListAsync(ct);

            var byParent = all
                .Where(c => c.ParentCategoryId is not null)
                .ToLookup(c => c.ParentCategoryId);

            return all
                .Where(c => c.ParentCategoryId is null)
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Name, PortugueseOrder)
                .Select(root => new CategoryNode(
                    root.Id, root.Name, root.Type, root.Icon, root.Color,
                    byParent[root.Id].OrderBy(c => c.Name, PortugueseOrder).ToList()))
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) =>
            Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<CategoryNode>>(200);
}
