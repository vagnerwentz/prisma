using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

// Quanto já foi estornado de uma compra e quanto ainda pode ser (docs/fase-2.md, 2.5, regra 7). Na
// compra parcelada, conta a compra inteira: o total dela e os estornos ligados a qualquer parcela.
public static class RefundAmounts
{
    public static async Task<RefundTarget> Target(AppDbContext db, Transaction purchase, Guid? exceptRefundId, CancellationToken ct) =>
        new(purchase, Refund.RefundableCents(
            await PurchaseCents(db, purchase, ct), await RefundedOf(db, purchase, exceptRefundId, ct)));

    // Estornos ativos ligados à compra; exceptRefundId fica de fora (o próprio estorno, ao editar).
    public static Task<long> RefundedOf(AppDbContext db, Transaction purchase, Guid? exceptRefundId, CancellationToken ct)
    {
        var refunds = purchase.InstallmentPurchaseId is { } purchaseId
            ? db.Transactions.Where(r => r.Type == TransactionType.Refund
                && db.Transactions.Any(p => p.Id == r.RefundedTransactionId && p.InstallmentPurchaseId == purchaseId))
            : db.Transactions.Where(r => r.Type == TransactionType.Refund && r.RefundedTransactionId == purchase.Id);

        if (exceptRefundId is { } except)
            refunds = refunds.Where(r => r.Id != except);

        return refunds.SumAsync(r => r.AmountCents, ct);
    }

    private static async Task<long> PurchaseCents(AppDbContext db, Transaction purchase, CancellationToken ct) =>
        purchase.InstallmentPurchaseId is { } purchaseId
            ? await db.InstallmentPurchases.Where(p => p.Id == purchaseId).Select(p => p.TotalAmountCents).SingleAsync(ct)
            : purchase.AmountCents;

    // Para a lista e o detalhe: o estornado e o que ainda pode ser estornado de cada despesa, numa
    // consulta só para a lista inteira.
    public static async Task<IReadOnlyList<TransactionResponse>> Fill(
        AppDbContext db, IReadOnlyList<TransactionResponse> transactions, CancellationToken ct)
    {
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        if (expenses.Count == 0)
            return transactions;

        var ids = expenses.Select(t => t.Id).ToList();
        var purchaseIds = expenses.Select(t => t.InstallmentPurchaseId).OfType<Guid>().Distinct().ToList();

        var refunds = await db.Transactions
            .AsNoTracking()
            .Where(r => r.Type == TransactionType.Refund)
            .Join(db.Transactions, r => r.RefundedTransactionId, p => p.Id,
                (r, p) => new { PurchaseTransactionId = p.Id, p.InstallmentPurchaseId, r.AmountCents })
            .Where(x => ids.Contains(x.PurchaseTransactionId)
                || (x.InstallmentPurchaseId != null && purchaseIds.Contains(x.InstallmentPurchaseId.Value)))
            .ToListAsync(ct);

        var totals = purchaseIds.Count == 0
            ? new Dictionary<Guid, long>()
            : await db.InstallmentPurchases.AsNoTracking()
                .Where(p => purchaseIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.TotalAmountCents, ct);

        var byPurchase = refunds.Where(x => x.InstallmentPurchaseId is not null)
            .GroupBy(x => x.InstallmentPurchaseId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountCents));
        var byTransaction = refunds.Where(x => x.InstallmentPurchaseId is null)
            .GroupBy(x => x.PurchaseTransactionId).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountCents));

        return transactions.Select(t =>
        {
            if (t.Type != TransactionType.Expense)
                return t;

            var (value, refunded) = t.InstallmentPurchaseId is { } purchaseId
                ? (totals.GetValueOrDefault(purchaseId), byPurchase.GetValueOrDefault(purchaseId))
                : (t.AmountCents, byTransaction.GetValueOrDefault(t.Id));
            return t with { RefundedCents = refunded, RefundableCents = Refund.RefundableCents(value, refunded) };
        }).ToList();
    }
}
