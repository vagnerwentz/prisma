using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// A carteira vista de quem põe um ativo nela: adicionar à mão (AddHolding) ou registrar um provento
// (docs/investimentos.md, etapa 5b). Não grava: quem chama salva junto com o resto.
internal static class Portfolio
{
    // Já está: devolve. Tinha sido tirado: volta o mesmo registro. Nunca esteve: cria.
    public static async Task<(Holding Holding, bool Changed)> Ensure(AppDbContext db, Guid userId, Guid assetId, CancellationToken ct)
    {
        var holding = await Find(db, assetId, ct);
        if (holding is null)
        {
            holding = Holding.Add(userId, assetId);
            db.Holdings.Add(holding);
            return (holding, true);
        }

        if (holding.DeletedAt is null)
            return (holding, false);

        holding.Restore();
        return (holding, true);
    }

    // Inclui o que foi tirado da carteira (regra 6: ignora só o soft delete; o filtro de dono continua).
    public static Task<Holding?> Find(AppDbContext db, Guid assetId, CancellationToken ct) =>
        db.Holdings
            .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
            .SingleOrDefaultAsync(h => h.AssetId == assetId, ct);
}
