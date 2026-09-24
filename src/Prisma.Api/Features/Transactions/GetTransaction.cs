using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Transactions;

public static class GetTransaction
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, CancellationToken ct)
        {
            var transaction = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(TransactionResponse.Projection)
                .SingleOrDefaultAsync(ct);

            return transaction is null
                ? new Error(ErrorType.NotFound, "Transação não encontrada.")
                : transaction;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
        {
            var result = await handler.Execute(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });
}
