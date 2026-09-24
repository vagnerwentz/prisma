using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Transactions;

public static class DeleteTransaction
{
    public sealed class Handler(AppDbContext db)
    {
        // Remove vira soft delete no AppDbContext; POST /transactions/{id}/restore desfaz.
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var transaction = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            if (transaction.CheckCanChangeIndividually() is { } error)
                return error;

            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync(ct);
            return transaction.Id;
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
