using Prisma.Domain.Market;

namespace Prisma.Domain.Investments.Trades;

// A posição de cada ativo, calculada das notas e dos eventos da empresa, nunca guardada (docs/investimentos.md, I.7a e
// I.7c1): como o saldo da conta sai dos lançamentos. Corrigir uma nota antiga refaz tudo o que vem depois.
//
// Notas em ordem de data (mesma data: a ordem recebida); dentro da nota, ordem a ordem, como aparecem. Compra soma
// quantidade e custos. Venda tira a fração vendida de cada custo (a que zera leva o resto, então a soma do que sai é
// sempre o que entrou) e não muda os preços médios do que fica.
//
// Evento vale no fim do dia da data, depois das notas do dia (quem compra na data-com tem direito): desdobramento e
// grupamento mudam a quantidade e mantêm os custos; conversão leva quantidade e custos inteiros para o destino, sem
// resultado. Nota de um ativo depois da conversão dele é recusada: ele não existe mais.
public static class PositionCalculator
{
    // symbolOf: o código do ativo, para as mensagens de recusa.
    public static Result<IReadOnlyList<AssetPosition>> Build(IEnumerable<TradeNote> notes, Func<Guid, string> symbolOf) =>
        Build(notes, [], symbolOf);

    public static Result<IReadOnlyList<AssetPosition>> Build(
        IEnumerable<TradeNote> notes, IEnumerable<CorporateEvent> events, Func<Guid, string> symbolOf)
    {
        var timeline = new Timeline();
        var ordered = events.OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
        var pending = new Queue<CorporateEvent>(ordered);
        var convertedOn = ordered.Where(e => e.Kind == CorporateEventKind.Conversion)
            .GroupBy(e => e.AssetId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var note in notes.OrderBy(n => n.Date))
        {
            while (pending.TryPeek(out var next) && next.Date < note.Date)
                timeline.Apply(pending.Dequeue());

            var costs = note.AllocateCosts();
            foreach (var trades in note.Orders())
            {
                var lines = trades.Select(i => note.Trades[i]).ToList();
                var first = lines[0];
                if (convertedOn.TryGetValue(first.AssetId, out var conversion) && note.Date > conversion.Date)
                    return Refused($"{symbolOf(first.AssetId)} virou {symbolOf(conversion.TargetAssetId!.Value)} em " +
                                   $"{conversion.Date:dd/MM/yyyy}. Desde então, lance como {symbolOf(conversion.TargetAssetId.Value)}.");

                var builder = timeline.Of(first.AssetId);
                var quantity = lines.Sum(t => t.Quantity);
                var gross = lines.Sum(t => t.GrossCents);
                var cost = trades.Sum(i => costs[i]);

                if (first.Side == TradeSide.Sell && quantity > builder.Totals.Quantity)
                    return Refused($"Venda de {quantity} {symbolOf(first.AssetId)} em {note.Date:dd/MM/yyyy}, mas a carteira tinha {builder.Totals.Quantity}.");

                builder.Trade(note, first.Side, quantity, gross, cost);
            }
        }

        while (pending.TryDequeue(out var remaining))
            timeline.Apply(remaining);

        return Result<IReadOnlyList<AssetPosition>>.Success(timeline.Positions());
    }

    private static Error Refused(string message) => new(ErrorType.Validation, message);

    // As posições na ordem em que cada ativo apareceu.
    private sealed class Timeline
    {
        private readonly Dictionary<Guid, Builder> _positions = [];
        private readonly List<Guid> _order = [];

        public Builder Of(Guid assetId)
        {
            if (!_positions.TryGetValue(assetId, out var builder))
            {
                _positions[assetId] = builder = new Builder();
                _order.Add(assetId);
            }

            return builder;
        }

        // Sem posição na data, o evento não deixa rastro.
        public void Apply(CorporateEvent e)
        {
            if (!_positions.TryGetValue(e.AssetId, out var source) || source.Totals.Quantity == 0)
                return;

            if (e.Kind == CorporateEventKind.Split)
            {
                source.Split(e);
                return;
            }

            var moved = source.Totals;
            source.ConvertOut(e);
            Of(e.TargetAssetId!.Value).ConvertIn(e, moved);
        }

        public IReadOnlyList<AssetPosition> Positions() => _order.Select(id => _positions[id].ToPosition(id)).ToList();
    }

    private sealed class Builder
    {
        private readonly List<PositionStep> _history = [];
        private long _realized;

        public PositionTotals Totals { get; private set; } = PositionTotals.Empty;

        public void Trade(TradeNote note, TradeSide side, long quantity, long gross, long costs)
        {
            long? realized = null;
            if (side == TradeSide.Buy)
            {
                Totals = new PositionTotals(
                    Totals.Quantity + quantity, Totals.GrossCostCents + gross, Totals.CostBasisCents + gross + costs);
            }
            else
            {
                var soldGross = Share(Totals.GrossCostCents, quantity);
                var soldBasis = Share(Totals.CostBasisCents, quantity);
                realized = gross - costs - soldBasis;
                _realized += realized.Value;
                Totals = new PositionTotals(
                    Totals.Quantity - quantity, Totals.GrossCostCents - soldGross, Totals.CostBasisCents - soldBasis);
            }

            _history.Add(new PositionStep(PositionStepKind.Trade, note.Date, note, null, null, side, quantity, gross, costs, Totals, realized));
        }

        // Os custos ficam; a quantidade muda pela proporção. A fração que sobra (vendida em leilão) fica sem custo
        // próprio por ora: o dinheiro dela é da I.7c2.
        public void Split(CorporateEvent e)
        {
            var before = Totals.Quantity;
            Totals = Totals with { Quantity = e.Convert(before) };
            _history.Add(new PositionStep(PositionStepKind.Split, e.Date, null, e, null, null, before, 0, 0, Totals, null));
        }

        public void ConvertOut(CorporateEvent e)
        {
            var before = Totals.Quantity;
            Totals = PositionTotals.Empty;
            _history.Add(new PositionStep(PositionStepKind.ConversionOut, e.Date, null, e, e.TargetAssetId, null, before, 0, 0, Totals, null));
        }

        // Não é venda: o custo inteiro da origem soma ao que já houver aqui.
        public void ConvertIn(CorporateEvent e, PositionTotals moved)
        {
            var received = e.Convert(moved.Quantity);
            Totals = new PositionTotals(
                Totals.Quantity + received, Totals.GrossCostCents + moved.GrossCostCents, Totals.CostBasisCents + moved.CostBasisCents);
            _history.Add(new PositionStep(PositionStepKind.ConversionIn, e.Date, null, e, e.AssetId, null, received, 0, 0, Totals, null));
        }

        // A parte do custo que sai com a venda. Vendendo tudo, a conta dá exatamente o custo inteiro.
        private long Share(long total, long sold) => PositionMath.RoundHalfUp((Int128)total * sold, Totals.Quantity);

        public AssetPosition ToPosition(Guid assetId) => new(assetId, Totals, _realized, _history);
    }
}
