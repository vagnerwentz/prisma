using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// "Confere" (docs/investimentos.md, I.7c2): um evento ou os que a lista mostra ("Conferir todos"). Só eventos que
// mexeram na quantidade da pessoa; qualquer outro, 404, nada gravado.
public static class ConfirmEvents
{
    public const int MaxEvents = 200;

    public sealed record Request(IReadOnlyList<Guid> EventIds);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(r => r.EventIds).NotEmpty().WithMessage("Escolha o evento.")
                .Must(ids => ids.Count <= MaxEvents).WithMessage($"Até {MaxEvents} eventos por vez.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, IClock clock, ILogger<Handler> logger)
    {
        public async Task<Result<int>> Execute(Request req, CancellationToken ct)
        {
            var effects = await EventReviewBook.Effects(db, clock.Today, ct);
            if (!effects.IsSuccess)
                return effects.Error;

            var byEvent = effects.Value.ToDictionary(e => e.Event.Id);
            var ids = req.EventIds.Distinct().ToList();
            if (ids.Any(id => !byEvent.ContainsKey(id)))
                return EventReviewBook.NotFound;

            foreach (var id in ids)
            {
                var reviewed = await EventReviewBook.Review(db, currentUser.UserId, byEvent[id], EventReviewVerdict.Confirmed, null, ct);
                if (!reviewed.IsSuccess)
                    return reviewed.Error;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (EventReviewBook.IsDuplicate(ex))
            {
                return new Error(ErrorType.Conflict, "Esse evento acabou de ser conferido. Atualize a tela.");
            }

            logger.EventReviewsConfirmed(ids.Count);
            return ids.Count;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/confirm", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces(204)
            .ProducesValidationProblem()
            .ProducesProblem(404)
            .ProducesProblem(409);
}
