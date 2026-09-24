using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Categories;

public static class CreateCategory
{
    public sealed record Request(
        string Name,
        TransactionType Type,
        Guid? ParentCategoryId,
        string? Icon,
        string? Color);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(x => x.Type).IsInEnum().WithMessage("Tipo de categoria inválido.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser)
    {
        public async Task<Result<CategoryResponse>> Execute(Request req, CancellationToken ct)
        {
            Category? parent = null;
            if (req.ParentCategoryId is { } parentId)
            {
                parent = await db.Categories.SingleOrDefaultAsync(c => c.Id == parentId, ct);
                if (parent is null)
                    return new Error(ErrorType.Validation, "Categoria pai não encontrada.");
            }

            var created = Category.Create(currentUser.UserId, req.Name, req.Type, parent, req.Icon, req.Color);
            if (!created.IsSuccess)
                return created.Error;

            db.Categories.Add(created.Value);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(CategoryConfiguration.SiblingNameIndex))
            {
                return Category.DuplicateName;
            }

            return CategoryResponse.From(created.Value);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess
                    ? Results.Created((string?)null, result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>();
}
