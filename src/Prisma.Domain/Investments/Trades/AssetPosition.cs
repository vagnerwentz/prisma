using Prisma.Domain.Market;

namespace Prisma.Domain.Investments.Trades;

// Quanto se tem de um ativo e quanto custou: custo bruto (só preço) e custo de aquisição (preço + custos).
// Os preços médios saem da divisão, em centavos, meio centavo para cima; sem quantidade, não há preço médio.
public sealed record PositionTotals(long Quantity, long GrossCostCents, long CostBasisCents)
{
    public static readonly PositionTotals Empty = new(0, 0, 0);

    // O que se compara com a cotação.
    public long? AveragePriceCents => Quantity == 0 ? null : PositionMath.RoundHalfUp(GrossCostCents, Quantity);

    // O da Receita e do resultado da venda.
    public long? AverageCostCents => Quantity == 0 ? null : PositionMath.RoundHalfUp(CostBasisCents, Quantity);
}

// Um passo do histórico, com a posição depois dele. Trade: uma ordem da nota (as linhas do mesmo ativo e direção
// juntas); na venda, o resultado (valor líquido − custo de aquisição que saiu). Evento da empresa (I.7c1): Split traz a
// quantidade de antes; ConversionOut, a que saiu para o destino (RelatedAssetId); ConversionIn, a que entrou vinda da
// origem. Evento não tem nota, direção nem valor.
// LiquidationOut (I.7c4): a origem saiu como venda; GrossCents é o valor recebido (cotas, fração e caixa) e RealizedCents
// o resultado. LiquidationIn: as cotas recebidas, com GrossCents igual ao custo delas.
public enum PositionStepKind { Trade, Split, ConversionOut, ConversionIn, LiquidationOut, LiquidationIn }

public sealed record PositionStep(
    PositionStepKind Kind,
    DateOnly Date,
    TradeNote? Note,
    CorporateEvent? Event,
    Guid? RelatedAssetId,
    TradeSide? Side,
    long Quantity,
    long GrossCents,
    long CostsCents,
    PositionTotals After,
    long? RealizedCents);

// A posição de um ativo, somando as corretoras (o preço médio da Receita é um por pessoa): a atual, o resultado
// das vendas e o histórico, do mais antigo ao mais recente. Fica na lista mesmo zerada.
public sealed record AssetPosition(Guid AssetId, PositionTotals Current, long RealizedCents, IReadOnlyList<PositionStep> History)
{
    // A posição no fim do dia (I.7e): depois do último passo até a data. O histórico já vem em ordem de data, com os
    // eventos depois das notas do dia.
    public PositionTotals On(DateOnly date) => History.LastOrDefault(s => s.Date <= date)?.After ?? PositionTotals.Empty;

    // O resultado das vendas com data no intervalo, contando as duas pontas.
    public long RealizedBetween(DateOnly from, DateOnly to) =>
        History.Where(s => s.Date >= from && s.Date <= to).Sum(s => s.RealizedCents ?? 0);
}

internal static class PositionMath
{
    // valor × parte ÷ todo, arredondado (meio para cima), sem estourar. Só para valores não negativos.
    public static long RoundHalfUp(Int128 numerator, Int128 denominator) =>
        (long)((2 * numerator + denominator) / (2 * denominator));
}
