using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Recurrences;

public static class ListRecurrences
{
    // As séries com a próxima data, que sai da agenda do domínio. As ativas primeiro, pela próxima data; as
    // encerradas no fim (docs/fase-2.md, 2.14, Tela).
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<RecurrenceResponse>> Execute(CancellationToken ct)
        {
            var recurrences = await db.Recurrences.AsNoTracking().ToListAsync(ct);
            var pendings = (await db.RecurrencePendings.AsNoTracking().ToListAsync(ct)).ToLookup(p => p.RecurrenceId);
            return recurrences
                .Select(r => RecurrenceResponse.From(r, pendings[r.Id]))
                .OrderBy(r => r.IsEnded)
                .ThenBy(r => r.NextOccurrence)
                .ThenBy(r => r.Description)
                .ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<RecurrenceResponse>>(200);
}
