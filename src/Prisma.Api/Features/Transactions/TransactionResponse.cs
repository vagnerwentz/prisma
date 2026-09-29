using System.Linq.Expressions;
using Prisma.Domain.Transactions;

namespace Prisma.Api.Features.Transactions;

public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    TransactionType Type,
    long AmountCents,
    DateOnly PurchaseDate,
    DateOnly SettlementDate,
    Guid? StatementId,
    Guid? CategoryId,
    PaymentMethod Method,
    string Description,
    string? RawDescription,
    Guid? InstallmentPurchaseId,
    int? InstallmentNumber,
    Guid? TransferPairId,
    TransferDirection? TransferDirection,
    TransactionSource Source,
    Guid? RefundedTransactionId,
    // Compra movida à mão para a fatura em que está (docs/fase-2.md, 2.9, regra 2).
    bool StatementPinned,
    // Só nas despesas, e só na lista e no detalhe (RefundAmounts.Fill): quanto já foi estornado e
    // quanto ainda pode ser; na compra parcelada, sobre o total da compra.
    long? RefundedCents,
    long? RefundableCents,
    // A série de que o lançamento faz parte (docs/fase-2.md, 2.14).
    Guid? RecurrenceId)
{
    public static readonly Expression<Func<Transaction, TransactionResponse>> Projection = t =>
        new TransactionResponse(
            t.Id, t.AccountId, t.Type, t.AmountCents, t.PurchaseDate, t.SettlementDate, t.StatementId,
            t.CategoryId, t.Method, t.Description, t.RawDescription, t.InstallmentPurchaseId,
            t.InstallmentNumber, t.TransferPairId, t.TransferDirection, t.Source, t.RefundedTransactionId, t.StatementPinned,
            null, null, t.RecurrenceId);

    private static readonly Func<Transaction, TransactionResponse> Compiled = Projection.Compile();

    public static TransactionResponse From(Transaction transaction) => Compiled(transaction);
}
