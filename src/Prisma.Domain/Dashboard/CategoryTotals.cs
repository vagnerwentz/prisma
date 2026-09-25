using Prisma.Domain.Transactions;

namespace Prisma.Domain.Dashboard;

// Soma de despesas ou de estornos de uma categoria raiz no mês (RootId nulo: sem categoria).
public readonly record struct CategoryAmount(Guid? RootId, TransactionType Type, long Cents);

public readonly record struct CategoryNet(Guid? RootId, long AmountCents);

// Gastos por categoria (docs/fase-2.md, 2.2 e 2.5, regra 12): o líquido de cada categoria raiz.
// Categoria com líquido zero ou negativo some; HiddenRefundCents é o estorno que sumiu com elas,
// de modo que Σ exibidas − HiddenRefundCents = despesas do mês.
public sealed record CategoryTotals(IReadOnlyList<CategoryNet> Shown, long HiddenRefundCents)
{
    public static CategoryTotals Of(IEnumerable<CategoryAmount> amounts)
    {
        var net = new Dictionary<Guid, long>();
        long uncategorized = 0;
        foreach (var amount in amounts)
        {
            var signed = amount.Type switch
            {
                TransactionType.Expense => amount.Cents,
                TransactionType.Refund => -amount.Cents,
                _ => throw new ArgumentException("Só despesas e estornos entram nos gastos por categoria.", nameof(amounts)),
            };
            if (amount.RootId is { } id) net[id] = net.GetValueOrDefault(id) + signed;
            else uncategorized += signed;
        }

        var all = net.Select(kv => new CategoryNet(kv.Key, kv.Value)).Append(new CategoryNet(null, uncategorized)).ToList();
        return new CategoryTotals(
            all.Where(c => c.AmountCents > 0).OrderByDescending(c => c.AmountCents).ToList(),
            -all.Where(c => c.AmountCents < 0).Sum(c => c.AmountCents));
    }
}
