using Prisma.Domain.Market;

namespace Prisma.Domain.Investments.Trades;

// A nota de corretagem digitada pela pessoa (o Prisma não importa nota): a corretora, as operações de um pregão e,
// embaixo, os custos (docs/investimentos.md, "I.7 Operações"). É o envelope do rateio: as operações da nota dividem
// os custos dela. Só mexe no saldo de uma conta se for vinculada a ela (SettlementAccountId, I.8).
public sealed class TradeNote : Entity
{
    public const int BrokerMaxLength = 60;

    // A regra de antes da escolha por tipo (I.7b3): só ação recebe a corretagem. É o que vem marcado na tela.
    public static readonly IReadOnlyList<AssetKind> StockOnly = [AssetKind.Stock];

    // Todos os tipos, com unit dentro de ação: a marcação antiga "vale também para FII, ETF e outros".
    public static readonly IReadOnlyList<AssetKind> AllKinds = Enum.GetValues<AssetKind>().Where(k => k != AssetKind.Unit).ToArray();

    private readonly List<Trade> _trades = [];

    private TradeNote() { }

    // O nome da corretora ("XP", "BTG Pactual", "Íon"); a tela acha o ícone pelo nome.
    public string Broker { get; private set; } = "";

    // O dia do pregão. Sem D+2: a carteira não mexe em dinheiro.
    public DateOnly Date { get; private set; }

    // A conta da corretora cujo caixa a nota mexe (I.8, docs/investimentos.md): nulo é "sem vínculo", o caso de toda nota
    // anterior à I.8. Quem confere a conta (do usuário, não cartão) é o chamador.
    public Guid? SettlementAccountId { get; private set; }

    public void LinkSettlementAccount(Guid? accountId) => SettlementAccountId = accountId;

    // Na ordem digitada.
    public IReadOnlyList<Trade> Trades => InTypedOrder();

    // Liquidação, emolumentos, registro, ISS, "outros" e qualquer custo novo: um total, dividido pelo valor. IRRF não
    // entra (é IR antecipado, não custo).
    public long FeesCents { get; private set; }

    // A "taxa operacional": por ordem. Não é a seção "Clearing" da nota, que é liquidação e registro (taxas).
    public long BrokerageFeeCents { get; private set; }

    // Os tipos em que a corretora cobrou a corretagem (I.7b3): só as ordens desses tipos a recebem. Sem Unit (conta
    // como Stock), sem repetição, na ordem do enum.
    public IReadOnlyList<AssetKind> BrokerageFeeKinds { get; private set; } = StockOnly;

    // Unit é ação para a corretagem.
    public static AssetKind BrokerageKindOf(AssetKind kind) => kind == AssetKind.Unit ? AssetKind.Stock : kind;

    // O pedido de antes da escolha por tipo manda só a marcação: ligada, todos os tipos; desligada ou ausente, só ação.
    public static IReadOnlyList<AssetKind> BrokerageKindsFrom(IReadOnlyCollection<AssetKind>? kinds, bool? onAllKinds) =>
        kinds is not null ? [.. kinds] : onAllKinds == true ? AllKinds : StockOnly;

    public static Result<TradeNote> Create(
        Guid userId, string broker, DateOnly date, IReadOnlyList<Trade> trades, long feesCents, long brokerageFeeCents,
        IReadOnlyCollection<AssetKind> brokerageFeeKinds)
    {
        var note = new TradeNote { UserId = userId };
        var error = note.Update(broker, date, trades, feesCents, brokerageFeeCents, brokerageFeeKinds);
        return error is null ? note : error;
    }

    // Troca tudo, linhas inclusive. Recusada, a nota fica como estava.
    public Error? Update(
        string broker, DateOnly date, IReadOnlyList<Trade> trades, long feesCents, long brokerageFeeCents,
        IReadOnlyCollection<AssetKind> brokerageFeeKinds)
    {
        var kinds = brokerageFeeKinds.Select(BrokerageKindOf).Distinct().Order().ToArray();

        broker = broker.Trim();
        if (broker.Length == 0)
            return Invalid("Escolha a corretora.");

        if (broker.Length > BrokerMaxLength)
            return Invalid($"O nome da corretora pode ter até {BrokerMaxLength} caracteres.");

        if (trades.Count == 0)
            return Invalid("Inclua ao menos uma operação na nota.");

        if (trades.Any(t => t.Quantity <= 0))
            return Invalid("A quantidade precisa ser maior que zero.");

        if (trades.Any(t => t.UnitPriceCents <= 0))
            return Invalid("O preço precisa ser maior que zero.");

        if (feesCents < 0 || brokerageFeeCents < 0)
            return Invalid("Os custos não podem ser negativos.");

        if (brokerageFeeCents > 0 && !trades.Any(t => kinds.Contains(t.BrokerageKind)))
            return Invalid("Marque em quais ativos a corretagem foi cobrada.");

        Broker = broker;
        Date = date;
        FeesCents = feesCents;
        BrokerageFeeCents = brokerageFeeCents;
        BrokerageFeeKinds = kinds;
        _trades.Clear();
        _trades.AddRange(trades);
        for (var i = 0; i < _trades.Count; i++)
            _trades[i].LineNumber = i + 1;
        return null;
    }

    public void Restore() => ClearDeletion();

    // Ordem = mesmo ativo + mesma direção, na ordem em que aparece. Preço diferente não separa: a nota lista
    // negócios, não ordens. Cada ordem traz os índices das suas linhas.
    public IEnumerable<IGrouping<(Guid AssetId, TradeSide Side), int>> Orders()
    {
        var trades = InTypedOrder();
        return Enumerable.Range(0, trades.Count).GroupBy(i => (trades[i].AssetId, trades[i].Side));
    }

    // O custo de cada operação, na ordem das operações. A soma é sempre taxas + corretagem.
    public IReadOnlyList<long> AllocateCosts()
    {
        InTypedOrder();
        var costs = new Money(FeesCents).Allocate(_trades.Select(t => t.GrossCents).ToArray())
            .Select(part => part.Cents).ToArray();

        var orders = Orders().Where(order => BrokerageFeeKinds.Contains(_trades[order.First()].BrokerageKind)).ToList();
        if (BrokerageFeeCents == 0 || orders.Count == 0)
            return costs;

        // Igual entre as ordens (centavo extra para a primeira); dentro da ordem, pelo valor de cada linha.
        var perOrder = new Money(BrokerageFeeCents).SplitInto(orders.Count);
        for (var o = 0; o < orders.Count; o++)
        {
            var lines = orders[o].ToArray();
            var parts = perOrder[o].Allocate(lines.Select(i => _trades[i].GrossCents).ToArray());
            for (var l = 0; l < lines.Length; l++)
                costs[lines[l]] += parts[l].Cents;
        }

        return costs;
    }

    // O que a corretora liquida na conta (I.8, docs/investimentos.md): as compras com os custos, menos as vendas já
    // sem os custos. Positivo, o dinheiro sai da conta; negativo, entra; zero, não há liquidação. Os custos entram
    // pelo mesmo rateio do preço médio (AllocateCosts), então a soma de uma nota nunca diverge da carteira.
    public long NetCashCents()
    {
        var costs = AllocateCosts();
        long net = 0;
        for (var i = 0; i < _trades.Count; i++)
        {
            var trade = _trades[i];
            net += trade.Side == TradeSide.Buy ? trade.GrossCents + costs[i] : -(trade.GrossCents - costs[i]);
        }

        return net;
    }

    // O banco devolve as linhas em qualquer ordem: arruma pelo número antes de usar.
    private List<Trade> InTypedOrder()
    {
        _trades.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return _trades;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
