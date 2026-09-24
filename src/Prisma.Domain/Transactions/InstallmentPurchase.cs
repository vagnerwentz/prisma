namespace Prisma.Domain.Transactions;

// Agrupa as parcelas de uma mesma compra no cartão (docs/fase-1.md). Nasce com 2+ parcelas; a
// edição pode reduzi-la a 1.
public sealed class InstallmentPurchase : Entity
{
    public const int MaxInstallments = 24;

    private InstallmentPurchase() { }

    public Guid AccountId { get; private set; }
    public string Description { get; private set; } = "";
    public long TotalAmountCents { get; private set; }
    public int InstallmentCount { get; private set; }
    public DateOnly PurchaseDate { get; private set; }

    internal static InstallmentPurchase Create(
        Guid userId, Guid accountId, string description, long totalAmountCents, int installmentCount,
        DateOnly purchaseDate) =>
        new()
        {
            UserId = userId,
            AccountId = accountId,
            Description = description,
            TotalAmountCents = totalAmountCents,
            InstallmentCount = installmentCount,
            PurchaseDate = purchaseDate,
        };

    internal void Update(long totalAmountCents, int installmentCount, string description)
    {
        TotalAmountCents = totalAmountCents;
        InstallmentCount = installmentCount;
        Description = description;
    }

    internal void Restore() => ClearDeletion();
}
