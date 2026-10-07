using Prisma.Domain.Market;

namespace Prisma.Domain.Investments.Trades;

// Uma linha da nota, como a pessoa digitou: "ITUB3, compra, 50 × R$ 20,00" (docs/investimentos.md, "I.7a Cálculo").
// O tipo do ativo é o do catálogo quando a nota foi gravada (I.7b1): decide quem recebe a corretagem, e uma
// reclassificação do fornecedor não muda notas antigas.
public sealed class Trade
{
    private Trade() { }

    public Trade(Guid assetId, AssetKind assetKind, TradeSide side, long quantity, long unitPriceCents)
    {
        AssetId = assetId;
        AssetKind = assetKind;
        Side = side;
        Quantity = quantity;
        UnitPriceCents = unitPriceCents;
    }

    public Guid AssetId { get; private set; }

    public AssetKind AssetKind { get; private set; }

    public TradeSide Side { get; private set; }

    public long Quantity { get; private set; }

    public long UnitPriceCents { get; private set; }

    // A posição na nota, a partir de 1. A nota numera ao receber as linhas.
    public int LineNumber { get; internal set; }

    public long GrossCents => checked(Quantity * UnitPriceCents);

    // Ação e unit pagam corretagem; os outros tipos, só com a marcação na nota.
    public bool IsStock => AssetKind is AssetKind.Stock or AssetKind.Unit;
}
