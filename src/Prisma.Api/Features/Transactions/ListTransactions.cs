using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Transactions;

public static class ListTransactions
{
    // O período filtra pela PurchaseDate: a lista mostra o que aconteceu em cada dia.
    // O dashboard (Fase 2) agrega pela SettlementDate.
    public sealed record Query(
        [FromQuery(Name = "from")] DateOnly? From,
        [FromQuery(Name = "to")] DateOnly? To,
        [FromQuery(Name = "accountId")] Guid? AccountId,
        [FromQuery(Name = "categoryId")] Guid? CategoryId,
        [FromQuery(Name = "search")] string? Search);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<TransactionResponse>>> Execute(Query query, CancellationToken ct)
        {
            if (query.From > query.To)
                return new Error(ErrorType.Validation, "A data inicial deve ser anterior ou igual à data final.");

            var transactions = db.Transactions.AsNoTracking();

            if (query.From is { } from)
                transactions = transactions.Where(t => t.PurchaseDate >= from);

            if (query.To is { } to)
                transactions = transactions.Where(t => t.PurchaseDate <= to);

            if (query.AccountId is { } accountId)
                transactions = transactions.Where(t => t.AccountId == accountId);

            // Filtrar por categoria inclui as subcategorias dela.
            if (query.CategoryId is { } categoryId)
            {
                var categoryIds = db.Categories
                    .Where(c => c.Id == categoryId || c.ParentCategoryId == categoryId)
                    .Select(c => (Guid?)c.Id);
                transactions = transactions.Where(t => categoryIds.Contains(t.CategoryId));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
                transactions = transactions.Where(t => EF.Functions.ILike(t.Description, pattern, @"\"));
            }

            return await transactions
                .OrderByDescending(t => t.PurchaseDate)
                .ThenByDescending(t => t.CreatedAt)
                .Select(TransactionResponse.Projection)
                .ToListAsync(ct);
        }

        private static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async ([AsParameters] Query query, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(query, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        })
            .Produces<IReadOnlyList<TransactionResponse>>(200)
            .ProducesProblem(400);
}
