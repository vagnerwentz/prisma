using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.FixedIncome;

// Exclui um aporte (soft delete, desfeito por POST /fixed-income-deposits/{id}/restore). Se o dinheiro saiu de uma conta pelo
// app (D3), a transferência existe à parte, e o que fazer com ela é de quem exclui:
//   ?transfer=keep    só o aporte sai; a transferência fica (o saldo da conta e o "Investido" não mudam);
//   ?transfer=delete  o aporte e a transferência saem juntos (o dinheiro volta para a conta), desfazíveis juntos.
// Com a transferência viva, a escolha é obrigatória (400, código TransferChoiceRequired). Aporte sem transferência não pergunta.
public static class DeleteDeposit
{
    public const string Keep = "keep";
    public const string Delete = "delete";

    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, string? transfer, CancellationToken ct)
        {
            if (transfer is not (null or Keep or Delete))
                return new Error(ErrorType.Validation, "Escolha manter ou excluir a transferência do aporte.");

            var deposit = await db.FixedIncomeDeposits.SingleOrDefaultAsync(d => d.Id == id, ct);
            if (deposit is null)
                return new Error(ErrorType.NotFound, "Aporte não encontrado.");

            var legs = deposit.TransferPairId is { } pair ? await Fundings.LiveLegs(db, pair, ct) : [];
            if (legs.Count > 0 && transfer is null)
                return new Error(
                    ErrorType.Validation,
                    "Este aporte saiu de uma conta. Escolha se a transferência fica ou sai junto.",
                    "TransferChoiceRequired");

            // As pontas e o aporte saem na mesma gravação, no mesmo instante: é o que o "Desfazer" usa para trazê-los juntos.
            if (transfer == Delete)
                db.Transactions.RemoveRange(legs);

            db.FixedIncomeDeposits.Remove(deposit);
            await db.SaveChangesAsync(ct);
            logger.DepositDeleted(deposit.Id);
            return deposit.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, string? transfer, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, transfer, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
