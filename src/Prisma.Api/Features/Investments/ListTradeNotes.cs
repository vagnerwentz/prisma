using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// As notas, da mais recente para a mais antiga (no mesmo dia, a última lançada primeiro); take limita
// ("Últimas notas"). E uma nota só, para abrir a partir do histórico do ativo.
public static class ListTradeNotes
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<TradeNoteResponse>> List(int? take, CancellationToken ct)
        {
            var query = db.TradeNotes.AsNoTracking()
                .OrderByDescending(n => n.Date).ThenByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id);
            var notes = await (take is > 0 ? query.Take(take.Value) : query).ToListAsync(ct);
            var assets = await TradeBook.Assets(db, notes.SelectMany(n => n.Trades).Select(t => t.AssetId), ct);
            return notes.Select(n => TradeNoteResponse.From(n, assets)).ToList();
        }

        public async Task<Result<TradeNoteResponse>> Get(Guid id, CancellationToken ct)
        {
            var note = await db.TradeNotes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct);
            if (note is null)
                return new Error(ErrorType.NotFound, "Nota não encontrada.");
            return TradeNoteResponse.From(note, await TradeBook.Assets(db, note.Trades.Select(t => t.AssetId), ct));
        }
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/", async (int? take, Handler handler, CancellationToken ct) => Results.Ok(await handler.List(take, ct)))
            .Produces<IReadOnlyList<TradeNoteResponse>>(200);

        app.MapGet("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Get(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<TradeNoteResponse>(200)
            .ProducesProblem(404);
    }
}
