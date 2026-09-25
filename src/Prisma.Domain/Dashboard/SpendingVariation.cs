using System.Globalization;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Dashboard;

// Um lançamento que entra no "Saiu" do mês: despesa ou estorno, com a categoria raiz (nula: sem
// categoria) e, na compra parcelada, a compra, o número da parcela e o total de parcelas.
public readonly record struct SpendingEntry(
    Guid TransactionId, TransactionType Type, Guid? RootCategoryId, string? RootCategoryName, string Description,
    long AmountCents, Guid? PurchaseId, int? InstallmentNumber, int? InstallmentCount)
{
    // Parcela 2 ou maior: a compra já cobrou numa fatura anterior (2.6).
    public bool IsInherited => Type == TransactionType.Expense && InstallmentNumber >= 2;
}

public enum VariationReasonKind { Inherited, Category, OtherCategories }

public readonly record struct EntryRef(Guid TransactionId, string Description, long AmountCents);

public readonly record struct PurchaseRef(Guid PurchaseId, string Description, long AmountCents);

// Um motivo da variação. Categoria que subiu traz o maior lançamento decidido no mês; as parcelas
// trazem as compras que começaram a ser herdadas no mês e as que terminaram no anterior.
public sealed record VariationReason(
    VariationReasonKind Kind, Guid? CategoryId, long ChangeCents, EntryRef? Largest,
    IReadOnlyList<PurchaseRef> Started, IReadOnlyList<PurchaseRef> Ended);

// Por que o gasto mudou (docs/fase-2.md, 2.7): a diferença do "Saiu" para o mês anterior decomposta
// em parcelas herdadas mais o decidido no mês por categoria raiz. Σ motivos = diferença, ao centavo.
public sealed record SpendingVariation(
    long ExpenseCents, long PreviousExpenseCents, long ChangeCents, IReadOnlyList<VariationReason> Reasons)
{
    public const int MaxCategories = 3;

    // Nomes usados só para desempatar a ordem, os mesmos que a tela mostra.
    private const string InheritedName = "Parcelas de compras anteriores";
    private const string UncategorizedName = "Sem categoria";

    private static readonly StringComparer PortugueseOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    public static SpendingVariation Of(IEnumerable<SpendingEntry> month, IEnumerable<SpendingEntry> previousMonth)
    {
        var current = Validated(month);
        var previous = Validated(previousMonth);

        var inheritedChange = Inherited(current) - Inherited(previous);
        var inheritedReason = new VariationReason(
            VariationReasonKind.Inherited, null, inheritedChange, null,
            Purchases(current.Where(e => e.IsInherited && e.InstallmentNumber == 2)),
            Purchases(previous.Where(e => e.IsInherited && e.InstallmentNumber == e.InstallmentCount)));

        var categories = DecidedByCategory(current, previous)
            .Where(c => c.Change != 0)
            .Select(c => (Reason: new VariationReason(
                VariationReasonKind.Category, c.Id, c.Change, c.Change > 0 ? LargestDecided(current, c.Id) : null, [], []), c.Name))
            .ToList();

        var ranked = Ranked(categories).ToList();
        var shown = ranked.Take(MaxCategories).ToList();
        var others = ranked.Skip(MaxCategories).Sum(c => c.Reason.ChangeCents);

        var candidates = shown;
        if (inheritedChange != 0) candidates = [.. candidates, (inheritedReason, InheritedName)];

        var reasons = Ranked(candidates).Select(c => c.Reason).ToList();
        if (others != 0) reasons.Add(new VariationReason(VariationReasonKind.OtherCategories, null, others, null, [], []));

        var expense = Expense(current);
        var previousExpense = Expense(previous);
        return new SpendingVariation(expense, previousExpense, expense - previousExpense, reasons);
    }

    private static List<SpendingEntry> Validated(IEnumerable<SpendingEntry> entries)
    {
        var list = entries.ToList();
        if (list.Any(e => e.Type is not (TransactionType.Expense or TransactionType.Refund)))
            throw new ArgumentException("Só despesas e estornos entram no \"Saiu\".", nameof(entries));
        return list;
    }

    private static long Signed(SpendingEntry e) => e.Type == TransactionType.Refund ? -e.AmountCents : e.AmountCents;

    private static long Expense(List<SpendingEntry> entries) => entries.Sum(Signed);

    private static long Inherited(List<SpendingEntry> entries) => entries.Where(e => e.IsInherited).Sum(e => e.AmountCents);

    // Decidido no mês por categoria: o líquido dela (despesas menos estornos) sem as parcelas herdadas.
    private static IEnumerable<(Guid? Id, string Name, long Change)> DecidedByCategory(
        List<SpendingEntry> current, List<SpendingEntry> previous)
    {
        // Entradas do mês somam, as do anterior subtraem: a soma por categoria já é a variação.
        return current.Where(e => !e.IsInherited).Select(e => (Entry: e, Change: Signed(e)))
            .Concat(previous.Where(e => !e.IsInherited).Select(e => (Entry: e, Change: -Signed(e))))
            .GroupBy(x => x.Entry.RootCategoryId)
            .Select(g => (
                g.Key,
                g.Key is null ? UncategorizedName : g.First().Entry.RootCategoryName ?? UncategorizedName,
                g.Sum(x => x.Change)));
    }

    // Maior variação em módulo primeiro; empate, alta antes de queda, depois pelo nome.
    private static IEnumerable<(VariationReason Reason, string Name)> Ranked(IEnumerable<(VariationReason Reason, string Name)> reasons) =>
        reasons
            .OrderByDescending(r => Math.Abs(r.Reason.ChangeCents))
            .ThenByDescending(r => r.Reason.ChangeCents > 0)
            .ThenBy(r => r.Name, PortugueseOrder)
            .ThenBy(r => r.Reason.CategoryId);

    private static EntryRef? LargestDecided(List<SpendingEntry> current, Guid? categoryId) =>
        current
            .Where(e => e.RootCategoryId == categoryId && e.Type == TransactionType.Expense && !e.IsInherited)
            .OrderByDescending(e => e.AmountCents)
            .ThenBy(e => e.Description, PortugueseOrder)
            .ThenBy(e => e.TransactionId)
            .Select(e => (EntryRef?)new EntryRef(e.TransactionId, e.Description, e.AmountCents))
            .FirstOrDefault();

    private static List<PurchaseRef> Purchases(IEnumerable<SpendingEntry> installments) =>
        installments
            .OrderByDescending(e => e.AmountCents)
            .ThenBy(e => e.Description, PortugueseOrder)
            .ThenBy(e => e.PurchaseId)
            .Select(e => new PurchaseRef(e.PurchaseId!.Value, e.Description, e.AmountCents))
            .ToList();
}
