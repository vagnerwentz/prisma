using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.FixedIncome;

// As caixas da pessoa em ordem de nome, com quantos aportes e quanto aportado, mais o que está fora de qualquer caixa
// (docs/renda-fixa.md, 2.2).
public static class ListGoals
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<GoalsResponse> Execute(CancellationToken ct)
        {
            var goals = await db.SavingsGoals.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var perGoal = await db.FixedIncomeDeposits.AsNoTracking()
                .Where(d => d.GoalId != null)
                .GroupBy(d => d.GoalId!.Value)
                .Select(g => new { GoalId = g.Key, Count = g.Count(), Principal = g.Sum(d => d.AmountCents) })
                .ToDictionaryAsync(g => g.GoalId, ct);
            var loose = await db.FixedIncomeDeposits.AsNoTracking()
                .Where(d => d.GoalId == null)
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Principal = g.Sum(d => d.AmountCents) })
                .SingleOrDefaultAsync(ct);

            // O aportado é o que sobra: o principal que os resgates já tiraram sai da soma (docs/renda-fixa-resgate.md, 4).
            var redeemed = await Goals.RedeemedPrincipalByGoal(db, ct);

            return new GoalsResponse(
                goals.Select(g => perGoal.TryGetValue(g.Id, out var t)
                    ? new GoalResponse(g.Id, g.Name, g.Description, g.TargetCents, g.DefaultIndexPercentE4, t.Count,
                        t.Principal - redeemed.GetValueOrDefault(g.Id))
                    : new GoalResponse(g.Id, g.Name, g.Description, g.TargetCents, g.DefaultIndexPercentE4, 0, 0)).ToList(),
                new UnassignedResponse(loose?.Count ?? 0, (loose?.Principal ?? 0) - redeemed.GetValueOrDefault(Guid.Empty)));
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<GoalsResponse>(200);
}
