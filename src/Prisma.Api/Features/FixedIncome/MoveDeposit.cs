using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.FixedIncome;

// Muda um aporte de caixa (ou o tira de todas, com goalId nulo) sem tocar no contrato dele: data, valor, percentual e histórico
// de rendimento seguem iguais (docs/renda-fixa.md, 2.2).
public static class MoveDeposit
{
    public sealed record Request(Guid? GoalId);

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<DepositResponse>> Execute(Guid id, Request req, CancellationToken ct)
        {
            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == id, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");
            if (req.GoalId is { } goalId && !await Goals.Exists(db, goalId, ct))
                return new Error(ErrorType.NotFound, "Objetivo não encontrado.");

            var from = deposit.GoalId;
            deposit.MoveTo(req.GoalId);
            await db.SaveChangesAsync(ct);
            logger.DepositMoved(deposit.Id, from, deposit.GoalId);
            return await Fundings.Respond(db, deposit, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/{id:guid}/goal", async (Guid id, Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<DepositResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
