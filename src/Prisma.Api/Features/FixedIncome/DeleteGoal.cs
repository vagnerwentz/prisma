using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.FixedIncome;

public sealed record DeletedGoalResponse(Guid Id, IReadOnlyList<Guid> DetachedDepositIds, IReadOnlyList<Guid> DeletedDepositIds);

// Excluir um objetivo (soft delete, desfeito por POST /savings-goals/{id}/restore). O objetivo é uma caixa: o que fazer com o
// que está dentro é de quem exclui (docs/renda-fixa.md, 2.2). Nunca se apaga investimento por baixo dos panos:
//   ?deposits=keep   só o objetivo sai; os aportes ficam, soltos ("Sem objetivo");
//   ?deposits=delete o objetivo e os aportes dele saem juntos (todos desfazíveis).
// Com aportes dentro, a escolha é obrigatória (400, código DepositsChoiceRequired): a tela pergunta antes.
public static class DeleteGoal
{
    public const string Keep = "keep";
    public const string Delete = "delete";

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<DeletedGoalResponse>> Execute(Guid id, string? deposits, CancellationToken ct)
        {
            if (deposits is not (null or Keep or Delete))
                return new Error(ErrorType.Validation, "Escolha manter ou excluir os investimentos do objetivo.");

            var goal = await db.SavingsGoals.SingleOrDefaultAsync(g => g.Id == id, ct);
            if (goal is null)
                return new Error(ErrorType.NotFound, "Objetivo não encontrado.");

            var inside = await db.FixedIncomeDeposits.Where(d => d.GoalId == id).ToListAsync(ct);
            if (inside.Count > 0 && deposits is null)
                return new Error(
                    ErrorType.Validation,
                    $"Este objetivo tem {inside.Count} investimento(s). Escolha se eles ficam ou saem junto.",
                    "DepositsChoiceRequired");

            var detached = new List<Guid>();
            var deleted = new List<Guid>();
            foreach (var deposit in inside)
            {
                if (deposits == Delete)
                {
                    db.FixedIncomeDeposits.Remove(deposit);
                    deleted.Add(deposit.Id);
                }
                else
                {
                    deposit.MoveTo(null);
                    detached.Add(deposit.Id);
                }
            }

            db.SavingsGoals.Remove(goal);
            await db.SaveChangesAsync(ct);
            logger.GoalDeleted(goal.Id, detached.Count, deleted.Count);
            return new DeletedGoalResponse(goal.Id, detached, deleted);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, string? deposits, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, deposits, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<DeletedGoalResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
