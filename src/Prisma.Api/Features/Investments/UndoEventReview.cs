using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// O "Desfazer" do "Confere" e do "Não confere" (docs/investimentos.md, I.7c2): soft delete, e o evento volta à lista.
public static class UndoEventReview
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid eventId, CancellationToken ct)
        {
            var review = await db.EventReviews.SingleOrDefaultAsync(r => r.CorporateEventId == eventId, ct);
            if (review is null)
                return EventReviewBook.NotFound;

            db.EventReviews.Remove(review);
            await db.SaveChangesAsync(ct);
            logger.EventReviewUndone(eventId);
            return eventId;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{eventId:guid}", async (Guid eventId, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(eventId, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(404);
}
