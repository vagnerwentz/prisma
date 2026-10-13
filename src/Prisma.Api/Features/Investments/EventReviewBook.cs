using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Domain;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// Os eventos que mexeram na quantidade da pessoa e a conferência dela (docs/investimentos.md, I.7c2). O que pede
// conferência é calculado da posição, nunca gravado.
internal static class EventReviewBook
{
    public static readonly Error NotFound = new(ErrorType.NotFound, "Evento não encontrado.");

    // Todos os eventos que afetaram a pessoa até hoje, com o que fizeram com a quantidade dela.
    public static async Task<Result<IReadOnlyList<EventEffect>>> Effects(AppDbContext db, DateOnly today, CancellationToken ct)
    {
        var notes = await TradeBook.Notes(db, ct);
        var positions = await TradeBook.Positions(db, notes, ct);
        if (!positions.IsSuccess)
            return new Error(ErrorType.Conflict, $"As notas não fecham: {positions.Error.Message} Corrija a nota dessa venda.");
        return Result<IReadOnlyList<EventEffect>>.Success(EventEffects.Of(positions.Value, today));
    }

    // Conferir ou não conferir um evento que afetou a pessoa; de novo, o mesmo registro (contando o desfeito).
    public static async Task<Result<bool>> Review(
        AppDbContext db, Guid userId, EventEffect effect, EventReviewVerdict verdict, long? reportedQuantity, CancellationToken ct)
    {
        var review = await db.EventReviews.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
            .SingleOrDefaultAsync(r => r.CorporateEventId == effect.Event.Id, ct);
        if (review is null)
        {
            var created = EventReview.Create(userId, effect.Event, verdict, reportedQuantity);
            if (!created.IsSuccess)
                return created.Error;
            db.EventReviews.Add(created.Value);
            return true;
        }

        var applied = review.Review(effect.Event, verdict, reportedQuantity);
        return applied.IsSuccess ? true : applied.Error;
    }

    // Dois toques ao mesmo tempo no mesmo evento: o segundo encontra o primeiro gravado.
    public static bool IsDuplicate(DbUpdateException ex) => ex.IsUniqueViolation(EventReviewConfiguration.EventIndex);
}
