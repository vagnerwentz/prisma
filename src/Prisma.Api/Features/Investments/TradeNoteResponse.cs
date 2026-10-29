using Prisma.Domain.Investments.Trades;
using Prisma.Domain.Market;

namespace Prisma.Api.Features.Investments;

// A nota como a tela mostra: as linhas na ordem digitada, cada uma com o ativo (do catálogo) e o custo que recebeu
// do rateio. AssetKind é o tipo gravado na linha, o que decidiu a corretagem. BrokerageFeeKinds: os tipos em que a
// corretagem foi cobrada (I.7b3); BrokerageFeeOnAllKinds fica para a tela de antes, calculado: ligado quando há operação
// que não é ação e todos os tipos da nota estão marcados.
public sealed record TradeNoteResponse(
    Guid Id, string Broker, DateOnly Date, long FeesCents, long BrokerageFeeCents, bool BrokerageFeeOnAllKinds,
    IReadOnlyList<TradeLineResponse> Trades, IReadOnlyList<AssetKind> BrokerageFeeKinds,
    // A conta que a nota liquida (I.8) e o que sai dela (positivo) ou entra (negativo); nulo e o líquido da nota, sem vínculo.
    Guid? SettlementAccountId, long NetCashCents)
{
    internal static TradeNoteResponse From(TradeNote note, IReadOnlyDictionary<Guid, TradeBook.AssetInfo> assets)
    {
        var costs = note.AllocateCosts();
        var lines = note.Trades.Select((t, i) =>
        {
            var asset = assets[t.AssetId];
            return new TradeLineResponse(
                t.AssetId, asset.Symbol, asset.Name, t.AssetKind, asset.HasLogo, t.Side, t.Quantity, t.UnitPriceCents, t.GrossCents, costs[i]);
        }).ToList();
        var onAllKinds = note.Trades.Any(t => t.BrokerageKind != AssetKind.Stock)
            && note.Trades.All(t => note.BrokerageFeeKinds.Contains(t.BrokerageKind));
        return new TradeNoteResponse(
            note.Id, note.Broker, note.Date, note.FeesCents, note.BrokerageFeeCents, onAllKinds, lines, note.BrokerageFeeKinds,
            note.SettlementAccountId, note.NetCashCents());
    }
}

public sealed record TradeLineResponse(
    Guid AssetId, string Symbol, string AssetName, AssetKind AssetKind, bool AssetHasLogo, TradeSide Side, long Quantity,
    long UnitPriceCents, long GrossCents, long CostsCents);
