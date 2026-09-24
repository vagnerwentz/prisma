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
    TransactionSource Source)
{
    public static readonly Expression<Func<Transaction, TransactionResponse>> Projection = t =>
        new TransactionResponse(
            t.Id, t.AccountId, t.Type, t.AmountCents, t.PurchaseDate, t.SettlementDate, t.StatementId,
            t.CategoryId, t.Method, t.Description, t.RawDescription, t.InstallmentPurchaseId,
            t.InstallmentNumber, t.TransferPairId, t.TransferDirection, t.Source);

    private static readonly Func<Transaction, TransactionResponse> Compiled = Projection.Compile();

    public static TransactionResponse From(Transaction transaction) => Compiled(transaction);
}
