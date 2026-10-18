using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.FixedIncome;

// O "Desfazer" da exclusão do aporte. Se a caixa dele foi excluída nesse meio-tempo, o aporte volta solto: nunca preso a uma
// caixa que a pessoa não vê. Se a transferência que o financiou saiu junto com ele (mesmo instante), volta junto; se uma das
// contas foi excluída nesse meio-tempo, nada volta e a resposta é 409.
public static class RestoreDeposit
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<DepositResponse>> Execute(Guid id, CancellationToken ct)
        {
            // Regra 6: ignora só o soft delete; o filtro de dono continua.
            var deposit = await db.FixedIncomeDeposits.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(d => d.Id == id, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");
            if (deposit.DeletedAt is null)
                return await Fundings.Respond(db, deposit, ct);

            var deletedAt = deposit.DeletedAt;
            deposit.Restore();
            if (deposit.GoalId is { } goalId && !await Goals.Exists(db, goalId, ct))
                deposit.MoveTo(null);

            if (deposit.TransferPairId is { } pair)
            {
                // As pontas excluídas junto com o aporte (o soft delete grava o mesmo instante nas três linhas).
                var legs = await db.Transactions.IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                    .Where(t => t.TransferPairId == pair && t.DeletedAt == deletedAt)
                    .ToListAsync(ct);
                if (legs.Count == 2)
                {
                    var accountIds = legs.Select(t => t.AccountId).ToList();
                    if (await db.Accounts.CountAsync(a => accountIds.Contains(a.Id), ct) != 2)
                        return new Error(ErrorType.Conflict, "Uma das contas deste aporte foi excluída; não é possível desfazer.");

                    var restored = Transfer.Restore(
                        new TransferLegs(
                            legs.Single(t => t.TransferDirection == TransferDirection.Out),
                            legs.Single(t => t.TransferDirection == TransferDirection.In)),
                        null, 0);
                    if (restored is not null)
                        return restored;
                }
            }

            await db.SaveChangesAsync(ct);
            logger.DepositRestored(deposit.Id);
            return await Fundings.Respond(db, deposit, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<DepositResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
