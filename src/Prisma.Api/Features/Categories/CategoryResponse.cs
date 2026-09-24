using System.Linq.Expressions;
using Prisma.Domain.Categories;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Categories;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    TransactionType Type,
    Guid? ParentCategoryId,
    string? Icon,
    string? Color)
{
    public static readonly Expression<Func<Category, CategoryResponse>> Projection = c =>
        new CategoryResponse(c.Id, c.Name, c.Type, c.ParentCategoryId, c.Icon, c.Color);

    private static readonly Func<Category, CategoryResponse> Compiled = Projection.Compile();

    public static CategoryResponse From(Category category) => Compiled(category);
}
