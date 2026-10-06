using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// Tira um ativo da carteira: soft delete. Pôr de novo (POST /holdings) devolve o mesmo registro.
public static class RemoveHolding
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var holding = await db.Holdings.SingleOrDefaultAsync(h => h.Id == id, ct);
            if (holding is null)
                return new Error(ErrorType.NotFound, "Ativo não encontrado na carteira.");

            db.Holdings.Remove(holding);
            await db.SaveChangesAsync(ct);
            logger.HoldingRemoved(holding.Id);
            return holding.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(404);
}
