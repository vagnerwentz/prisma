using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// Cancela o aviso que espera ou dispensa o que chegou: soft delete, desfeito por POST /asset-requests/{id}/restore.
public static class RemoveAssetRequest
{
    public sealed class Handler(AppDbContext db, ILogger<Handler> logger)
    {
        public async Task<Result<Guid>> Execute(Guid id, CancellationToken ct)
        {
            var request = await db.AssetRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
            if (request is null)
                return new Error(ErrorType.NotFound, "Aviso não encontrado.");

            db.AssetRequests.Remove(request);
            await db.SaveChangesAsync(ct);
            logger.AssetRequestRemoved(request.Id);
            return request.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/{id:guid}", async (Guid id, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(204)
            .ProducesProblem(404);
}
