using System.Globalization;

namespace Prisma.Domain.Dashboard;

// Uma parcela de compra parcelada que vence no mês, com o número dela e o total de parcelas.
public readonly record struct InstallmentCharge(
    Guid TransactionId, Guid PurchaseId, string Description, Guid? CategoryId, Guid AccountId,
    int Number, int Count, long AmountCents, DateOnly PurchaseDate);

// Parcelas de compras anteriores (docs/fase-2.md, 2.6): a parte do "Saiu" do mês que já estava
// decidida. Herdada é a parcela 2 ou maior, porque a compra já cobrou numa fatura anterior; a parcela 1
// conta como compra do mês. Decidido no mês é o "Saiu" menos o herdado, e some quando os estornos
// deixam o "Saiu" abaixo do herdado.
public sealed record InheritedInstallments(long InheritedCents, long? DecidedInMonthCents, IReadOnlyList<InstallmentCharge> Charges)
{
    private static readonly StringComparer PortugueseOrder =
        StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    public static InheritedInstallments Of(long monthExpenseCents, IEnumerable<InstallmentCharge> charges)
    {
        var inherited = charges
            .Where(c => c.Number >= 2)
            .OrderByDescending(c => c.AmountCents)
            .ThenBy(c => c.PurchaseDate)
            .ThenBy(c => c.Description, PortugueseOrder)
            .ThenBy(c => c.TransactionId)
            .ToList();

        var inheritedCents = inherited.Sum(c => c.AmountCents);
        long? decided = monthExpenseCents >= inheritedCents ? monthExpenseCents - inheritedCents : null;
        return new InheritedInstallments(inheritedCents, decided, inherited);
    }
}
