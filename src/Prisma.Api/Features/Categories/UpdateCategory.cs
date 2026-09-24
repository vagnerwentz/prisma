using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Categories;

namespace Prisma.Api.Features.Categories;

public static class UpdateCategory
{
    // Todos os campos editáveis juntos, como em /accounts. Tipo e pai não são editáveis.
    public sealed record Request(string Name, string? Icon, string? Color);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<CategoryResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (category is null)
                return new Error(ErrorType.NotFound, "Categoria não encontrada.");

            var updated = category.Update(req.Name, req.Icon, req.Color);
            if (!updated.IsSuccess)
                return updated.Error;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(CategoryConfiguration.SiblingNameIndex))
            {
                return Category.DuplicateName;
            }

            return CategoryResponse.From(category);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });
}
