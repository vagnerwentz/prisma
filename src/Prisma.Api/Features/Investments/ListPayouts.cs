using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;

namespace Prisma.Api.Features.Investments;

// Todos os proventos, do mais recente ao mais antigo (docs/investimentos.md, etapa 5b). A tela soma o ano, o total
// de cada ativo e agrupa por dia: são poucos por mês (um por ativo), e uma consulta só serve a tela inteira.
public static class ListPayouts
{
    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<PayoutResponse>> Execute(CancellationToken ct)
        {
            var rows = await db.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId != null)
                .Join(db.Assets.AsNoTracking(), t => t.AssetId, a => a.Id, (payout, asset) => new { payout, asset })
                .OrderByDescending(x => x.payout.PurchaseDate)
                .ThenByDescending(x => x.payout.CreatedAt)
                .Select(x => new { x.payout, x.asset, hasLogo = db.AssetLogos.Any(l => l.AssetId == x.asset.Id) })
                .ToListAsync(ct);
            return rows.Select(x => PayoutResponse.From(x.payout, x.asset, x.hasLogo)).ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async (Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(ct)))
            .Produces<IReadOnlyList<PayoutResponse>>(200);
}
