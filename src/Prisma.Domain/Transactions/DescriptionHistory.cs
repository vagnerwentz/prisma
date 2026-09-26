using System.Globalization;
using System.Text;

namespace Prisma.Domain.Transactions;

// Um lançamento como fonte de sugestão. Na compra parcelada, o número da parcela e o total da compra.
public readonly record struct DescriptionUse(
    TransactionType Type, string Description, Guid? CategoryId, Guid AccountId, PaymentMethod Method, long AmountCents,
    DateOnly PurchaseDate, int? InstallmentNumber, long? PurchaseTotalCents, DateTime CreatedAt);

// Uma descrição já usada, com o que valeu da última vez.
public sealed record DescriptionSuggestion(
    string Description, TransactionType Type, Guid? CategoryId, Guid AccountId, PaymentMethod Method,
    long LastAmountCents, int Count, DateOnly LastUsedOn);

// O vocabulário do autocompletar (docs/fase-2.md, 2.8): as descrições dos últimos 12 meses,
// agrupadas sem acento, maiúscula ou espaço sobrando; de cada grupo vale o lançamento mais recente.
public static class DescriptionHistory
{
    public const int MaxItems = 300;

    private static readonly StringComparer PortugueseOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    // Primeiro dia que conta: hoje menos 12 meses.
    public static DateOnly Since(DateOnly today) => today.AddMonths(-12);

    // "  Farmácia   São João " → "farmacia sao joao".
    public static string Key(string description)
    {
        var decomposed = string.Join(' ', description.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Normalize(NormalizationForm.FormD);
        var key = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                key.Append(char.ToLowerInvariant(ch));
        return key.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<DescriptionSuggestion> Of(IEnumerable<DescriptionUse> uses, DateOnly today)
    {
        var since = Since(today);
        return uses
            .Where(u => u.Type is TransactionType.Expense or TransactionType.Income)
            .Where(u => u.PurchaseDate >= since && !string.IsNullOrWhiteSpace(u.Description))
            // Compra parcelada conta uma vez, com o total: é o valor que se digita em Lançar.
            .Where(u => u.InstallmentNumber is null or 1)
            .GroupBy(u => (u.Type, Key: Key(u.Description)))
            .Select(g =>
            {
                var last = g.OrderByDescending(u => u.PurchaseDate).ThenByDescending(u => u.CreatedAt).First();
                return new DescriptionSuggestion(
                    last.Description.Trim(), last.Type, last.CategoryId, last.AccountId, last.Method,
                    last.PurchaseTotalCents ?? last.AmountCents, g.Count(), last.PurchaseDate);
            })
            .OrderByDescending(s => s.Count)
            .ThenByDescending(s => s.LastUsedOn)
            .ThenBy(s => s.Description, PortugueseOrder)
            .Take(MaxItems)
            .ToList();
    }
}
