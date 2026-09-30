using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Transactions;

// Conferir o valor de um débito automático (docs/fase-2.md, 2.15, regra 6): sem valor, a estimativa vira o
// valor; com valor, ele substitui a estimativa. O item sai do sino.
public static class ConfirmAmount
{
    public sealed record Request(long? AmountCents);

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<TransactionResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var transaction = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (transaction is null)
                return new Error(ErrorType.NotFound, "Transação não encontrada.");

            var estimate = transaction.AmountCents;
            var confirmed = transaction.ConfirmAmount(req.AmountCents);
            if (!confirmed.IsSuccess)
                return confirmed.Error;

            await db.SaveChangesAsync(ct);
            logger.AmountConfirmed(transaction.Id, corrected: transaction.AmountCents != estimate);
            return TransactionResponse.From(transaction);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/confirm-amount", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<TransactionResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
