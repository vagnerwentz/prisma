using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Recurrences;

public static class DiscardRecurrencePending
{
    // Descartar a cobrança pendente (docs/fase-2.md, 2.14, regra 9): nada é lançado, e ela não volta (o
    // índice da pendência conta as excluídas, e a série já passou dessa data).
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var pending = await db.RecurrencePendings.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (pending is null)
                return new Error(ErrorType.NotFound, "Pendência não encontrada.");

            db.RecurrencePendings.Remove(pending);
            await db.SaveChangesAsync(ct);
            logger.RecurrencePendingDiscarded(pending.Id);
            return pending.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/pendings/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(404);
}
