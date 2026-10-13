using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// A carteira atual, calculada das notas (docs/investimentos.md, "I.7b1 API"): um item por ativo com nota, em ordem
// de código, mesmo zerado (o resultado das vendas continua). E o histórico de um ativo, um passo por ordem.
public static class ListPositions
{
    // IsActive (I.7c1): o ativo que virou outro mostra "saiu da bolsa".
    public sealed record PositionResponse(
        Guid AssetId, string Symbol, string Name, AssetKind Kind, bool HasLogo, bool IsActive, long Quantity, long GrossCostCents,
        long CostBasisCents, long? AveragePriceCents, long? AverageCostCents, long RealizedCents);

    // Kind: Trade (uma ordem da nota) ou um evento da empresa (I.7c1). No evento, NoteId, Broker e Side vêm nulos, e
    // RelatedSymbol (o outro lado da conversão), FromQuantity e ToQuantity (a proporção) vêm preenchidos.
    public sealed record StepResponse(
        PositionStepKind Kind, Guid? NoteId, DateOnly Date, string? Broker, TradeSide? Side, long Quantity, long GrossCents,
        long CostsCents, long QuantityAfter, long GrossCostAfterCents, long CostBasisAfterCents, long? AveragePriceCents,
        long? AverageCostCents, long? RealizedCents, string? RelatedSymbol, int? FromQuantity, int? ToQuantity);

    public sealed class Handler(AppDbContext db)
    {
        public async Task<Result<IReadOnlyList<PositionResponse>>> List(CancellationToken ct)
        {
            var notes = await TradeBook.Notes(db, ct);
            var positions = await TradeBook.Positions(db, notes, ct);
            if (!positions.IsSuccess)
                return Inconsistent(positions.Error);

            var assets = await TradeBook.Assets(db, positions.Value.Select(p => p.AssetId), ct);
            return positions.Value
                .Select(p =>
                {
                    var asset = assets[p.AssetId];
                    var c = p.Current;
                    return new PositionResponse(p.AssetId, asset.Symbol, asset.Name, asset.Kind, asset.HasLogo, asset.IsActive, c.Quantity,
                        c.GrossCostCents, c.CostBasisCents, c.AveragePriceCents, c.AverageCostCents, p.RealizedCents);
                })
                .OrderBy(p => p.Symbol, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<Result<IReadOnlyList<StepResponse>>> History(Guid assetId, CancellationToken ct)
        {
            var notes = await TradeBook.Notes(db, ct);
            var positions = await TradeBook.Positions(db, notes, ct);
            if (!positions.IsSuccess)
                return Inconsistent(positions.Error);

            var position = positions.Value.SingleOrDefault(p => p.AssetId == assetId);
            if (position is null)
                return Array.Empty<StepResponse>();

            var related = await TradeBook.Assets(db, position.History.Where(s => s.RelatedAssetId is not null).Select(s => s.RelatedAssetId!.Value), ct);
            return position.History
                .Select(s => new StepResponse(s.Kind, s.Note?.Id, s.Date, s.Note?.Broker, s.Side, s.Quantity, s.GrossCents, s.CostsCents,
                    s.After.Quantity, s.After.GrossCostCents, s.After.CostBasisCents, s.After.AveragePriceCents,
                    s.After.AverageCostCents, s.RealizedCents, s.RelatedAssetId is { } id ? related[id].Symbol : null,
                    s.Event?.FromQuantity, s.Event?.ToQuantity))
                .ToList();
        }

        // Só acontece se duas gravações ao mesmo tempo passaram cada uma sem a outra (corrida aceita na I.7b1):
        // a pessoa vê qual venda corrigir em vez de um número errado.
        private static Error Inconsistent(Error error) =>
            new(ErrorType.Conflict, $"As notas não fecham: {error.Message} Corrija a nota dessa venda.");
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/", async (Handler handler, CancellationToken ct) =>
            {
                var result = await handler.List(ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<PositionResponse>>(200)
            .ProducesProblem(409);

        app.MapGet("/{assetId:guid}", async (Guid assetId, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.History(assetId, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<IReadOnlyList<StepResponse>>(200)
            .ProducesProblem(409);
    }
}
