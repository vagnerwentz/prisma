using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Investments;

// Põe um ativo na carteira (docs/investimentos.md, seção 8, etapa 5a). Já está: não faz nada. Tinha sido tirado:
// volta o mesmo registro, e é assim que o "Desfazer" funciona. Dois pedidos ao mesmo tempo esbarram no índice
// único, e o segundo devolve o que o primeiro criou.
public static class AddHolding
{
    public sealed record Request(Guid AssetId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.AssetId).NotEmpty().WithMessage("Escolha um ativo.");
    }

    public sealed class Handler(AppDbContext db, ICurrentUser currentUser, ILogger<Handler> logger)
    {
        public async Task<Result<HoldingResponse>> Execute(Request req, CancellationToken ct)
        {
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == req.AssetId, ct);
            if (asset is null)
                return new Error(ErrorType.NotFound, "Ativo não encontrado.");

            var hasLogo = await db.AssetLogos.AnyAsync(l => l.AssetId == asset.Id, ct);
            var (holding, changed) = await Portfolio.Ensure(db, currentUser.UserId, asset.Id, ct);
            if (!changed)
                return HoldingResponse.From(holding, asset, hasLogo);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(HoldingConfiguration.AssetIndex))
            {
                db.ChangeTracker.Clear();
                holding = (await Portfolio.Find(db, req.AssetId, ct))!;
                return HoldingResponse.From(holding, asset, hasLogo);
            }

            logger.HoldingAdded(holding.Id, asset.Id);
            return HoldingResponse.From(holding, asset, hasLogo);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<Request>>()
            .Produces<HoldingResponse>(200)
            .ProducesValidationProblem()
            .ProducesProblem(404);
}
