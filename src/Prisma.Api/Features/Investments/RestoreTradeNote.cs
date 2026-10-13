using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments.Trades;

namespace Prisma.Api.Features.Investments;

// O "Desfazer" da exclusão. Confere a carteira como uma nota nova: a venda restaurada precisa das compras que
// existem hoje. Os ativos voltam para a carteira.
public static class RestoreTradeNote
{
    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public async Task<Result<TradeNoteResponse>> Execute(Guid id, CancellationToken ct)
        {
            // Regra 6: ignora só o soft delete; o filtro de dono continua.
            var note = await db.TradeNotes
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(n => n.Id == id, ct);
            if (note is null)
                return new Error(ErrorType.NotFound, "Nota não encontrada.");
            if (note.DeletedAt is null)
                return await Response(note, ct);

            var notes = await TradeBook.Notes(db, ct);
            note.Restore();
            notes = [.. notes.Append(note).OrderBy(n => n.Date).ThenBy(n => n.CreatedAt).ThenBy(n => n.Id)];
            var positions = await TradeBook.Positions(db, notes, ct);
            if (!positions.IsSuccess)
                return positions.Error;

            foreach (var assetId in note.Trades.Select(t => t.AssetId).Distinct())
                await Portfolio.Ensure(db, currentUser.UserId, assetId, ct);
            await db.SaveChangesAsync(ct);
            logger.TradeNoteRestored(note.Id);
            return await Response(note, ct);
        }

        private async Task<TradeNoteResponse> Response(TradeNote note, CancellationToken ct) =>
            TradeNoteResponse.From(note, await TradeBook.Assets(db, note.Trades.Select(t => t.AssetId), ct));
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<TradeNoteResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
