using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Categories;

public static class DeleteCategory
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (category is null)
                return new Error(ErrorType.NotFound, "Categoria não encontrada.");

            // O filtro global já ignora subcategorias e transações excluídas.
            var activeSubcategories = await db.Categories.CountAsync(c => c.ParentCategoryId == id, ct);
            var activeTransactions = await db.Transactions.CountAsync(t => t.CategoryId == id, ct);
            if (category.CheckCanDelete(activeSubcategories, activeTransactions) is { } error)
                return error;

            db.Categories.Remove(category);
            await db.SaveChangesAsync(ct);
            return category.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        })
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
