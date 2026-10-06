using System.Linq.Expressions;
using Prisma.Domain.Investments;
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
    Guid? RecurrenceId,
    // Débito automático com o valor estimado, a conferir (docs/fase-2.md, 2.15, regras 5 a 8).
    bool AmountEstimated,
    // Provento (docs/investimentos.md, etapa 5b): o ativo e o tipo. O código do ativo vem só na lista e no
    // detalhe (PayoutAssets.Fill).
    Guid? AssetId = null,
    PayoutKind? PayoutKind = null,
    string? AssetSymbol = null,
    bool AssetHasLogo = false,
    Prisma.Domain.Market.AssetKind? AssetKind = null)
{
    public static readonly Expression<Func<Transaction, TransactionResponse>> Projection = t =>
        new TransactionResponse(
            t.Id, t.AccountId, t.Type, t.AmountCents, t.PurchaseDate, t.SettlementDate, t.StatementId,
            t.CategoryId, t.Method, t.Description, t.RawDescription, t.InstallmentPurchaseId,
            t.InstallmentNumber, t.TransferPairId, t.TransferDirection, t.Source, t.RefundedTransactionId, t.StatementPinned,
            null, null, t.RecurrenceId, t.AmountEstimated, t.AssetId, t.PayoutKind, null);

    private static readonly Func<Transaction, TransactionResponse> Compiled = Projection.Compile();

    public static TransactionResponse From(Transaction transaction) => Compiled(transaction);
}
