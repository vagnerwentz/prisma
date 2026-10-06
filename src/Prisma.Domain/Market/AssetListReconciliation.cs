namespace Prisma.Domain.Market;

// Confronta a lista do fornecedor com o catálogo (docs/investimentos.md, seção 4):
//   - código novo entra;
//   - código conhecido atualiza nome, nome longo e tipo, e volta a ser ativo se tinha sumido;
//   - código que não veio fica inativo, e nunca é apagado;
//   - o fracionário ("BBAS3F") é o mesmo ativo do lote padrão (BBAS3): entra com o código dele, uma vez só.
//
// Trava de sanidade: lista com menos da metade dos ativos do catálogo é tida por quebrada (fornecedor num
// dia ruim, resposta truncada) e não muda nada. Sem a trava, uma lista vazia inativaria o catálogo inteiro.
public static class AssetListReconciliation
{
    public const double MinimumShareOfActive = 0.5;

    public static AssetListChanges Apply(IReadOnlyCollection<Asset> catalog, IReadOnlyList<ListedAsset> listed, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("O instante precisa estar em UTC (DateTimeKind.Utc).", nameof(utcNow));

        // O primeiro de cada código vale. O lote padrão vem antes do fracionário (OrderBy é estável): quando os
        // dois estão na lista, vale o que o fornecedor diz do lote padrão.
        var bySymbol = new Dictionary<string, ListedAsset>(StringComparer.Ordinal);
        foreach (var asset in listed.OrderBy(a => AssetSymbol.IsFractional(a.Symbol)))
        {
            var symbol = AssetSymbol.BaseOf(asset.Symbol);
            bySymbol.TryAdd(symbol, symbol == asset.Symbol ? asset : asset.WithSymbol(symbol));
        }

        var active = catalog.Count(a => a.IsActive);
        if (bySymbol.Count < active * MinimumShareOfActive)
            return AssetListChanges.Refused(active, bySymbol.Count);

        var known = catalog.ToDictionary(a => a.Symbol, StringComparer.Ordinal);
        var added = new List<Asset>();
        int updated = 0, reactivated = 0, deactivated = 0;

        foreach (var item in bySymbol.Values)
        {
            if (!known.TryGetValue(item.Symbol, out var asset))
            {
                added.Add(Asset.From(item, utcNow));
                continue;
            }

            switch (asset.Refresh(item, utcNow))
            {
                case AssetRefresh.Updated:
                    updated++;
                    break;
                case AssetRefresh.Reactivated:
                    reactivated++;
                    break;
            }
        }

        foreach (var asset in catalog.Where(a => !bySymbol.ContainsKey(a.Symbol)))
            if (asset.Deactivate(utcNow))
                deactivated++;

        return new AssetListChanges(false, active, bySymbol.Count, added, updated, reactivated, deactivated);
    }
}

// Refused: a trava de sanidade recusou a lista, e nada mudou. ActiveBefore e Listed explicam a recusa no log.
public sealed record AssetListChanges(
    bool IsRefused,
    int ActiveBefore,
    int Listed,
    IReadOnlyList<Asset> Added,
    int Updated,
    int Reactivated,
    int Deactivated)
{
    internal static AssetListChanges Refused(int activeBefore, int listed) =>
        new(true, activeBefore, listed, [], 0, 0, 0);
}
