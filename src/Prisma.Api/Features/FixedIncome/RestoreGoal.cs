using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.FixedIncome;

// O "Desfazer" da exclusão do objetivo. Volta o objetivo e:
//  - os aportes que saíram junto com ele (excluídos no mesmo instante), com tudo o que tinham;
//  - os aportes que ficaram soltos ao excluir (a tela manda os ids que recebeu na exclusão), mas só os que continuam soltos:
//    quem a pessoa já pôs em outra caixa nesse meio-tempo fica onde está.
// Se outro objetivo já usa o nome, 409: renomear o outro e desfazer de novo.
public static class RestoreGoal
{
    public sealed record Request(IReadOnlyList<Guid>? DetachedDepositIds);

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<GoalResponse>> Execute(Guid id, Request? req, CancellationToken ct)
        {
            // Regra 6: ignora só o soft delete; o filtro de dono continua.
            var goal = await db.SavingsGoals.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).SingleOrDefaultAsync(g => g.Id == id, ct);
            if (goal is null)
                return new Error(ErrorType.NotFound, "Objetivo não encontrado.");
            if (goal.DeletedAt is null)
                return await Goals.Summary(db, goal, ct);

            var deletedAt = goal.DeletedAt;
            goal.Restore();

            // O soft delete grava o mesmo instante no objetivo e nos aportes que saíram com ele.
            var together = await db.FixedIncomeDeposits.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .Where(d => d.GoalId == id && d.DeletedAt == deletedAt)
                .ToListAsync(ct);
            foreach (var deposit in together)
                deposit.Restore();

            var ids = req?.DetachedDepositIds ?? [];
            var detached = ids.Count == 0
                ? []
                : await db.FixedIncomeDeposits.Where(d => ids.Contains(d.Id) && d.GoalId == null).ToListAsync(ct);
            foreach (var deposit in detached)
                deposit.MoveTo(id);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(SavingsGoalConfiguration.NameIndex))
            {
                return new Error(
                    ErrorType.Conflict, "Já existe um objetivo com esse nome. Renomeie o outro para desfazer.", "GoalNameTaken");
            }

            logger.GoalRestored(goal.Id, together.Count, detached.Count);
            return await Goals.Summary(db, goal, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore",
                async (Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] Request? request, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.Execute(id, request, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
                })
            .Produces<GoalResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
