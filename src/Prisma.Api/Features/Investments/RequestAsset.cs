using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Investments;

namespace Prisma.Api.Features.Investments;

// "Não encontrou? Avise a gente" (docs/investimentos.md, I.7c3). Código que já está no catálogo não vira pedido. Já
// avisou: devolve o mesmo pedido; tinha cancelado: volta o mesmo registro. Dois toques ao mesmo tempo esbarram no
// índice único, e o segundo devolve o que o primeiro criou.
public static class RequestAsset
{
    public sealed record Request(string? Symbol);

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public async Task<Result<AssetRequestResponse>> Execute(Request req, CancellationToken ct)
        {
            var symbol = AssetRequest.Normalize(req.Symbol);
            if (!symbol.IsSuccess)
                return symbol.Error;
            if (await db.Assets.AnyAsync(a => a.Symbol == symbol.Value, ct))
                return new Error(ErrorType.Conflict, $"{symbol.Value} já está no Prisma. Busque de novo.");

            var request = await Find(symbol.Value, ct);
            if (request is { DeletedAt: null })
                return await AssetRequestBook.Describe(db, request, ct);
            if (await AssetRequestBook.Waiting(db, ct) >= AssetRequest.MaxWaiting)
                return AssetRequestBook.TooMany;

            if (request is null)
            {
                request = AssetRequest.Create(currentUser.UserId, symbol.Value).Value;
                db.AssetRequests.Add(request);
            }
            else
            {
                request.Restore();
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(AssetRequestConfiguration.SymbolIndex))
            {
                db.ChangeTracker.Clear();
                return await AssetRequestBook.Describe(db, (await Find(symbol.Value, ct))!, ct);
            }

            logger.AssetRequested(request.Id, request.Symbol);
            return await AssetRequestBook.Describe(db, request, ct);
        }

        // Inclui os cancelados (regra 6: ignora só o soft delete; o filtro de dono continua).
        private Task<AssetRequest?> Find(string symbol, CancellationToken ct) =>
            db.AssetRequests
                .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(r => r.Symbol == symbol, ct);
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.AssetRequestsRateLimitPolicy)
            .Produces<AssetRequestResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(429);
}
