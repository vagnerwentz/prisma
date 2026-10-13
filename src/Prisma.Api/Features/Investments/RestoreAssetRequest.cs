using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// O "Desfazer" de cancelar ou dispensar um aviso. O limite de avisos esperando vale também aqui.
public static class RestoreAssetRequest
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<AssetRequestResponse>> Execute(Guid id, CancellationToken ct)
        {
            // Regra 6: ignora só o soft delete; o filtro de dono continua.
            var request = await db.AssetRequests
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(r => r.Id == id, ct);
            if (request is null)
                return new Error(ErrorType.NotFound, "Aviso não encontrado.");
            if (request.DeletedAt is null)
                return await AssetRequestBook.Describe(db, request, ct);

            var waiting = !await db.Assets.AnyAsync(a => a.Symbol == request.Symbol, ct);
            if (waiting && await AssetRequestBook.Waiting(db, ct) >= AssetRequest.MaxWaiting)
                return AssetRequestBook.TooMany;

            request.Restore();
            await db.SaveChangesAsync(ct);
            logger.AssetRequestRestored(request.Id);
            return await AssetRequestBook.Describe(db, request, ct);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/{id:guid}/restore", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<AssetRequestResponse>(200)
            .ProducesProblem(404)
            .ProducesProblem(409);
}
