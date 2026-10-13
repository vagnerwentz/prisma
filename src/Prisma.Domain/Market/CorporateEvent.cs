namespace Prisma.Domain.Market;

// Desdobramento e grupamento (Split: cada `from` vira `to`, 1:8 ou 10:1) e conversão (incorporação ou troca de
// código: `from` do ativo vira `to` do destino, com o custo). docs/investimentos.md, I.7c1. Liquidação paga em cotas
// (I.7c4): a origem sai como venda e o destino entra como compra pelo preço de referência (BCFF11 → BTHF11 a R$ 10,25).
// Bonificação e subscrição vêm depois.
public enum CorporateEventKind { Split, Conversion, Liquidation }

// Evento da empresa: a regra (proporção, data), nunca a quantidade de alguém. É dado de mercado, igual para todos, como
// o Asset: sem dono, só a curadoria escreve (o arquivo de eventos, CorporateEventReconciliation).
//
// Date é a data-com: o evento vale no fim desse dia, para quem tinha o ativo (quem compra na data-com tem direito).
public sealed class CorporateEvent
{
    public const int KeyMaxLength = 80;

    // EF Core.
    private CorporateEvent()
    {
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    // Estável no arquivo de eventos: é por ela que a sincronização reconhece o evento.
    public string Key { get; private init; } = null!;

    public CorporateEventKind Kind { get; private set; }

    public Guid AssetId { get; private set; }

    // Na conversão e na liquidação: o ativo que a pessoa passa a ter.
    public Guid? TargetAssetId { get; private set; }

    public DateOnly Date { get; private set; }

    public int FromQuantity { get; private set; }

    public int ToQuantity { get; private set; }

    // Só na liquidação (I.7c4), em centésimos de milionésimo de real (10.25 → 1_025_000_000): o valor de cada cota
    // recebida, que vira o custo dela, e o dinheiro por cota da origem (ausente: zero). Mais casas que centavos porque
    // o documento traz (R$ 83,3946076); só o produto vira centavo.
    public long? TargetUnitPrice { get; private set; }

    public long? CashPerUnit { get; private set; }

    public const long UnitValueScale = 100_000_000;

    // De onde saiu o evento (fato relevante, comunicado), para quem revisar.
    public string Source { get; private set; } = null!;

    public DateTime CreatedAt { get; private init; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<CorporateEvent> Create(
        string key, CorporateEventKind kind, Guid assetId, Guid? targetAssetId, DateOnly date, int fromQuantity, int toQuantity,
        string source, DateTime utcNow, long? targetUnitPrice = null, long? cashPerUnit = null)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > KeyMaxLength)
            return Invalid($"Evento sem chave, ou com mais de {KeyMaxLength} caracteres.");

        var definition = new CorporateEventDefinition(key, kind, assetId, targetAssetId, date, fromQuantity, toQuantity, source, targetUnitPrice, cashPerUnit);
        if (Validate(definition) is { } error)
            return error;

        var created = new CorporateEvent { Key = key, CreatedAt = utcNow };
        created.Set(definition, utcNow);
        return created;
    }

    // A quantidade depois do evento, para baixo: a fração que sobra a B3 vende em leilão.
    public long Convert(long quantity) => (long)((Int128)quantity * ToQuantity / FromQuantity);

    // Devolve se algo mudou (a curadoria corrigiu o evento).
    internal bool Update(CorporateEventDefinition definition, DateTime utcNow)
    {
        if (Kind == definition.Kind && AssetId == definition.AssetId && TargetAssetId == definition.TargetAssetId
            && Date == definition.Date && FromQuantity == definition.FromQuantity && ToQuantity == definition.ToQuantity
            && Source == definition.Source && TargetUnitPrice == definition.TargetUnitPrice && CashPerUnit == definition.CashPerUnit)
            return false;

        Set(definition, utcNow);
        return true;
    }

    internal static Error? Validate(CorporateEventDefinition d)
    {
        if (d.FromQuantity <= 0 || d.ToQuantity <= 0)
            return Invalid($"Evento {d.Key}: a proporção precisa ser maior que zero.");

        return d.Kind switch
        {
            CorporateEventKind.Split when d.TargetAssetId is not null => Invalid($"Evento {d.Key}: desdobramento não tem ativo de destino."),
            CorporateEventKind.Conversion when d.TargetAssetId is null || d.TargetAssetId == d.AssetId =>
                Invalid($"Evento {d.Key}: a conversão precisa de outro ativo de destino."),
            CorporateEventKind.Liquidation when d.TargetAssetId is null || d.TargetAssetId == d.AssetId =>
                Invalid($"Evento {d.Key}: a liquidação precisa de outro ativo de destino."),
            CorporateEventKind.Liquidation when d.TargetUnitPrice is not > 0 =>
                Invalid($"Evento {d.Key}: a liquidação precisa do preço de referência do destino, maior que zero."),
            CorporateEventKind.Liquidation when d.CashPerUnit < 0 => Invalid($"Evento {d.Key}: o caixa por cota não pode ser negativo."),
            not CorporateEventKind.Liquidation when d.TargetUnitPrice is not null || d.CashPerUnit is not null =>
                Invalid($"Evento {d.Key}: preço e caixa por cota são só da liquidação."),
            _ => null,
        };
    }

    private void Set(CorporateEventDefinition d, DateTime utcNow)
    {
        Kind = d.Kind;
        AssetId = d.AssetId;
        TargetAssetId = d.TargetAssetId;
        Date = d.Date;
        FromQuantity = d.FromQuantity;
        ToQuantity = d.ToQuantity;
        Source = d.Source;
        TargetUnitPrice = d.TargetUnitPrice;
        CashPerUnit = d.CashPerUnit;
        UpdatedAt = utcNow;
    }

    // Valor por cota como o arquivo traz: decimal com ponto, até 8 casas, sem sinal ("10.25", "83.3946076"). Até R$ 1
    // bilhão por cota, para o produto pela quantidade caber com folga.
    public static bool TryParseUnitValue(string text, out long value)
    {
        value = 0;
        var parts = text.Split('.');
        if (parts.Length > 2 || parts[0].Length is 0 or > 10 || !parts[0].All(char.IsAsciiDigit))
            return false;
        var decimals = parts.Length == 2 ? parts[1] : "";
        if (parts.Length == 2 && (decimals.Length is 0 or > 8 || !decimals.All(char.IsAsciiDigit)))
            return false;
        var whole = long.Parse(parts[0]);
        if (whole >= 1_000_000_000)
            return false;
        value = whole * UnitValueScale + (decimals.Length == 0 ? 0 : long.Parse(decimals.PadRight(8, '0')));
        return true;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}

// Um evento como está no arquivo, já com os ativos resolvidos.
public sealed record CorporateEventDefinition(
    string Key, CorporateEventKind Kind, Guid AssetId, Guid? TargetAssetId, DateOnly Date, int FromQuantity, int ToQuantity, string Source,
    long? TargetUnitPrice = null, long? CashPerUnit = null);
