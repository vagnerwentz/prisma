namespace Prisma.Domain.Market;

// Desdobramento e grupamento (Split: cada `from` vira `to`, 1:8 ou 10:1) e conversão (incorporação ou troca de
// código: `from` do ativo vira `to` do destino). docs/investimentos.md, I.7c1. Bonificação e subscrição vêm depois.
public enum CorporateEventKind { Split, Conversion }

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

    // Só na conversão: o ativo que a pessoa passa a ter.
    public Guid? TargetAssetId { get; private set; }

    public DateOnly Date { get; private set; }

    public int FromQuantity { get; private set; }

    public int ToQuantity { get; private set; }

    // De onde saiu o evento (fato relevante, comunicado), para quem revisar.
    public string Source { get; private set; } = null!;

    public DateTime CreatedAt { get; private init; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<CorporateEvent> Create(
        string key, CorporateEventKind kind, Guid assetId, Guid? targetAssetId, DateOnly date, int fromQuantity, int toQuantity,
        string source, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > KeyMaxLength)
            return Invalid($"Evento sem chave, ou com mais de {KeyMaxLength} caracteres.");

        var definition = new CorporateEventDefinition(key, kind, assetId, targetAssetId, date, fromQuantity, toQuantity, source);
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
            && Source == definition.Source)
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
        UpdatedAt = utcNow;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}

// Um evento como está no arquivo, já com os ativos resolvidos.
public sealed record CorporateEventDefinition(
    string Key, CorporateEventKind Kind, Guid AssetId, Guid? TargetAssetId, DateOnly Date, int FromQuantity, int ToQuantity, string Source);
