using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Recurrences;

public static class EndRecurrence
{
    // Encerrar: o término vai para o último lançamento gerado, e nada mais é gerado; o que já foi fica
    // (docs/fase-2.md, 2.14, regra 7). Se a tarefa gerar ao mesmo tempo, o xmin da série dá 409.
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<RecurrenceResponse>> Execute(Guid id, CancellationToken ct)
        {
            var recurrence = await db.Recurrences.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (recurrence is null)
                return new Error(ErrorType.NotFound, "Série não encontrada.");

            recurrence.End();
            await db.SaveChangesAsync(ct);
            logger.RecurrenceEnded(recurrence.Id);
            var pendings = await db.RecurrencePendings.AsNoTracking().Where(p => p.RecurrenceId == id).ToListAsync(ct);
            return RecurrenceResponse.From(recurrence, pendings);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/end", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<RecurrenceResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
