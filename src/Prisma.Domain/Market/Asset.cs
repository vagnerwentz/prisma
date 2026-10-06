namespace Prisma.Domain.Market;

// Ativo da bolsa no catálogo do Prisma (docs/investimentos.md, seção 3). É dado de mercado, o mesmo para
// todo mundo: não tem dono nem soft delete, por isso não herda Entity (CLAUDE.md, regra 6, exceção dos dados
// de mercado). Só a sincronização diária escreve (AssetListReconciliation); o usuário só lê.
public sealed class Asset
{
    // EF Core.
    private Asset()
    {
    }

    private Asset(ListedAsset listed, DateTime utcNow)
    {
        Symbol = listed.Symbol;
        Name = listed.Name;
        LongName = listed.LongName;
        Kind = listed.Kind;
        LogoUrl = listed.LogoUrl;
        SearchText = AssetSearch.TextOf(Symbol, Name, LongName);
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    // A chave é nossa: o código muda (VIIA3 virou BHIA3), e o que aponta para o ativo não pode mudar junto.
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    // Único. É por ele que a sincronização reconhece o ativo; não muda depois de criado.
    public string Symbol { get; private init; } = null!;

    public string Name { get; private set; } = null!;

    public string? LongName { get; private set; }

    public AssetKind Kind { get; private set; }

    // Onde o fornecedor publica o logo; o logo em si, limpo, fica em AssetLogo (etapa 4). Nulo: sem logo.
    public string? LogoUrl { get; private set; }

    // Código e nomes sem acento e em minúsculas, para a busca (AssetSearch). Acompanha os nomes.
    public string SearchText { get; private set; } = null!;

    // O nome que se mostra. Na ação e na unit, o nome da empresa; no fundo e no BDR, o nome longo, porque o
    // nome curto deles costuma ser o próprio código ("MXRF11").
    public string DisplayName => Kind is AssetKind.Stock or AssetKind.Unit ? Name : LongName ?? Name;

    // Quando sumiu da lista do fornecedor; nulo enquanto é negociado. O ativo nunca é apagado: pode haver
    // rendimento ou operação apontando para ele.
    public DateTime? InactiveSince { get; private set; }

    public bool IsActive => InactiveSince is null;

    public DateTime CreatedAt { get; private init; }

    // Muda só quando algo muda de fato: a sincronização diária não regrava os 2.337 ativos sem motivo.
    public DateTime UpdatedAt { get; private set; }

    internal static Asset From(ListedAsset listed, DateTime utcNow) => new(listed, utcNow);

    // Devolve o que aconteceu, para a contagem da sincronização.
    internal AssetRefresh Refresh(ListedAsset listed, DateTime utcNow)
    {
        var reactivated = !IsActive;
        var changed = Name != listed.Name || LongName != listed.LongName || Kind != listed.Kind || LogoUrl != listed.LogoUrl;
        if (!reactivated && !changed)
            return AssetRefresh.Unchanged;

        Name = listed.Name;
        LongName = listed.LongName;
        Kind = listed.Kind;
        LogoUrl = listed.LogoUrl;
        SearchText = AssetSearch.TextOf(Symbol, Name, LongName);
        InactiveSince = null;
        UpdatedAt = utcNow;
        return reactivated ? AssetRefresh.Reactivated : AssetRefresh.Updated;
    }

    internal bool Deactivate(DateTime utcNow)
    {
        if (!IsActive)
            return false;

        InactiveSince = utcNow;
        UpdatedAt = utcNow;
        return true;
    }
}

internal enum AssetRefresh { Unchanged, Updated, Reactivated }
