using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// Tira um ativo da carteira: soft delete. Pôr de novo (POST /holdings) devolve o mesmo registro. Com ações pelas notas,
// recusa: a carteira tem que bater com as notas (decisão do dono, docs/investimentos.md, I.7b).
public static class RemoveHolding
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var holding = await db.Holdings.SingleOrDefaultAsync(h => h.Id == id, ct);
            if (holding is null)
                return new Error(ErrorType.NotFound, "Ativo não encontrado na carteira.");

            var notes = await TradeBook.Notes(db, ct);
            var positions = await TradeBook.Positions(db, notes, ct);
            var held = positions.IsSuccess
                ? positions.Value.SingleOrDefault(p => p.AssetId == holding.AssetId)?.Current.Quantity ?? 0
                : 0;
            if (held > 0)
            {
                var symbol = (await TradeBook.Assets(db, [holding.AssetId], ct))[holding.AssetId].Symbol;
                return new Error(ErrorType.Conflict, $"Ainda há {held} {symbol} pelas notas. Só sai da carteira com a posição zerada.");
            }

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
            .ProducesProblem(404)
            .ProducesProblem(409);
}
