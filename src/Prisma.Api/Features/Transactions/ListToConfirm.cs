using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Transactions;

// O sino (docs/fase-2.md, 2.15, D4): os lançamentos com o valor estimado, o mais antigo primeiro. Não há
// caixa de avisos nem estado de lido: cada um sai da lista quando é conferido ou excluído.
public static class ListToConfirm
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<TransactionResponse>> Execute(CancellationToken ct) =>
            await db.Transactions
                .AsNoTracking()
                .Where(t => t.AmountEstimated)
                .OrderBy(t => t.PurchaseDate)
                .ThenBy(t => t.Id)
                .Select(TransactionResponse.Projection)
                .ToListAsync(ct);
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/to-confirm", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<TransactionResponse>>(200);
}
