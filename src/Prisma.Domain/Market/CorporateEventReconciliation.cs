namespace Prisma.Domain.Market;

// Confronta o arquivo de eventos (a curadoria) com a tabela: entra o que é novo, muda o que mudou, sai o que saiu do
// arquivo. Arquivo com evento inválido ou chave repetida não muda nada: melhor os eventos de ontem que um pela metade.
public static class CorporateEventReconciliation
{
    public static Result<CorporateEventChanges> Apply(
        IReadOnlyCollection<CorporateEvent> current, IReadOnlyList<CorporateEventDefinition> file, DateTime utcNow)
    {
        var duplicated = file.GroupBy(d => d.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            return new Error(ErrorType.Validation, $"Evento {duplicated.Key} repetido no arquivo.");

        foreach (var definition in file)
            if (CorporateEvent.Validate(definition) is { } error)
                return error;

        var known = current.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var added = new List<CorporateEvent>();
        var updated = 0;
        foreach (var definition in file)
        {
            if (known.TryGetValue(definition.Key, out var existing))
            {
                if (existing.Update(definition, utcNow))
                    updated++;
                continue;
            }

            var created = CorporateEvent.Create(
                definition.Key, definition.Kind, definition.AssetId, definition.TargetAssetId, definition.Date,
                definition.FromQuantity, definition.ToQuantity, definition.Source, utcNow, definition.TargetUnitPrice, definition.CashPerUnit);
            if (!created.IsSuccess)
                return created.Error;
            added.Add(created.Value);
        }

        var keys = file.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var removed = current.Where(e => !keys.Contains(e.Key)).ToList();
        return new CorporateEventChanges(added, updated, removed);
    }
}

public sealed record CorporateEventChanges(IReadOnlyList<CorporateEvent> Added, int Updated, IReadOnlyList<CorporateEvent> Removed);
