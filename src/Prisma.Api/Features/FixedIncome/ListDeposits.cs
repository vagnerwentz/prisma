using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.FixedIncome;

// Os aportes, do mais recente ao mais antigo. ?goal=<id> traz os de uma caixa; ?goal=none, os que estão fora de qualquer
// caixa; sem o parâmetro, todos.
public static class ListDeposits
{
    public const string None = "none";

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<DepositResponse>>> Execute(string? goal, CancellationToken ct)
        {
            var query = db.FixedIncomeDeposits.AsNoTracking();
            if (string.IsNullOrEmpty(goal))
            {
            }
            else if (goal == None)
            {
                query = query.Where(d => d.GoalId == null);
            }
            else if (Guid.TryParse(goal, out var goalId))
            {
                query = query.Where(d => d.GoalId == goalId);
            }
            else
            {
                return new Error(ErrorType.Validation, "Objetivo inválido.");
            }

            var rows = await query.OrderByDescending(d => d.DepositDate).ThenByDescending(d => d.CreatedAt).ToListAsync(ct);
            return Result<IReadOnlyList<DepositResponse>>.Success(await Fundings.Respond(db, rows, ct));
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (string? goal, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(goal, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<DepositResponse>>(200)
            .ProducesProblem(400);
}
