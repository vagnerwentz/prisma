using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// Exclui a nota (soft delete, desfeito por POST /trade-notes/{id}/restore). Recusada se uma venda depois dela
// precisava das compras dela (docs/investimentos.md, "I.7b1 API").
public static class DeleteTradeNote
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var note = await db.TradeNotes.SingleOrDefaultAsync(n => n.Id == id, ct);
            if (note is null)
                return new Error(ErrorType.NotFound, "Nota não encontrada.");

            var notes = await TradeBook.Notes(db, ct);
            var positions = await TradeBook.Positions(db, notes.Where(n => n.Id != id).ToList(), ct);
            if (!positions.IsSuccess)
                return positions.Error;

            db.TradeNotes.Remove(note);
            await db.SaveChangesAsync(ct);
            logger.TradeNoteDeleted(note.Id);
            return note.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
