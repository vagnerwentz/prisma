using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// "Não confere" (docs/investimentos.md, I.7c2): registra para a curadoria, com a quantidade que a corretora mostra
// (opcional). A carteira não muda: o evento vale até a curadoria corrigir ou tirar.
public static class DisputeEvent
{
    public sealed record Request(long? ReportedQuantity);

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, IClock clock, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid eventId, Request req, CancellationToken ct)
        {
            var effects = await EventReviewBook.Effects(db, clock.Today, ct);
            if (!effects.IsSuccess)
                return effects.Error;

            var effect = effects.Value.FirstOrDefault(e => e.Event.Id == eventId);
            if (effect is null)
                return EventReviewBook.NotFound;

            var reviewed = await EventReviewBook.Review(db, currentUser.UserId, effect, EventReviewVerdict.Disputed, req.ReportedQuantity, ct);
            if (!reviewed.IsSuccess)
                return reviewed.Error;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (EventReviewBook.IsDuplicate(ex))
            {
                return new Error(ErrorType.Conflict, "Esse evento acabou de ser conferido. Atualize a tela.");
            }

            logger.EventReviewDisputed(eventId);
            return eventId;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{eventId:guid}/dispute", async (Guid eventId, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(eventId, request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
