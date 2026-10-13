using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// As notas, da mais recente para a mais antiga (no mesmo dia, a última lançada primeiro); take limita
// ("Últimas notas"), e year fica só com as notas daquele ano (I.7e). E uma nota só, para abrir a partir do histórico do ativo.
public static class ListTradeNotes
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<TradeNoteResponse>> List(int? take, int? year, CancellationToken ct)
        {
            var notes = db.TradeNotes.AsNoTracking();
            if (year is { } y)
            {
                // Intervalo de datas, e não Date.Year: usa o índice da data.
                var (first, last) = (new DateOnly(Math.Clamp(y, 1, 9999), 1, 1), new DateOnly(Math.Clamp(y, 1, 9999), 12, 31));
                notes = notes.Where(n => n.Date >= first && n.Date <= last);
            }
            var query = notes
                .OrderByDescending(n => n.Date).ThenByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id);
            var found = await (take is > 0 ? query.Take(take.Value) : query).ToListAsync(ct);
            var assets = await TradeBook.Assets(db, found.SelectMany(n => n.Trades).Select(t => t.AssetId), ct);
            return found.Select(n => TradeNoteResponse.From(n, assets)).ToList();
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
        app.MapGet("/", async (int? take, int? year, Handler handler, CancellationToken ct) => Results.Ok(await handler.List(take, year, ct)))
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
