using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Transactions;

// AmountCents é sempre positivo; o Type define se entra ou sai (docs/fase-1.md).
public sealed class Transaction : Entity
{
    public const int DescriptionMaxLength = 200;

    private Transaction() { }

    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public long AmountCents { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public DateOnly SettlementDate { get; private set; }
    public Guid? StatementId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string Description { get; private set; } = "";
    public string? RawDescription { get; private init; }
    public Guid? InstallmentPurchaseId { get; private set; }
    public int? InstallmentNumber { get; private set; }
    public Guid? TransferPairId { get; private set; }
    public TransactionSource Source { get; private init; }

    // Receita ou despesa fora do cartão: o dinheiro sai (ou entra) no dia da compra.
    public static Result<Transaction> CreateSimple(
        Guid userId, Account account, TransactionType type, long amountCents, DateOnly purchaseDate,
        Category? category, PaymentMethod method, string? description)
    {
        if (ValidateSimple(account, type, amountCents, category, method, description) is { } error)
            return error;

        var transaction = new Transaction { UserId = userId, Source = TransactionSource.Manual };
        transaction.ApplySimple(account, type, amountCents, purchaseDate, category, method, description);
        return transaction;
    }

    public Result<Transaction> UpdateSimple(
        Account account, TransactionType type, long amountCents, DateOnly purchaseDate,
        Category? category, PaymentMethod method, string? description)
    {
        if (StatementId is not null)
            return Invalid("Lançamento em cartão de crédito usa a compra no cartão.");

        if (ValidateSimple(account, type, amountCents, category, method, description) is { } error)
            return error;

        ApplySimple(account, type, amountCents, purchaseDate, category, method, description);
        return this;
    }

    // Parcela de compra parcelada só é excluída ou restaurada junto com a compra, para a soma
    // das parcelas continuar igual ao total.
    public Error? CheckCanChangeIndividually() =>
        InstallmentPurchaseId is null
            ? null
            : new Error(ErrorType.Conflict, "Esta parcela faz parte de uma compra parcelada. Exclua a compra inteira.");

    // Criada só por CardPurchase, que valida a compra inteira antes.
    internal static Transaction CreateCardInstallment(
        Guid userId, Account card, long amountCents, DateOnly purchaseDate, Statement statement,
        Category? category, string description, Guid? installmentPurchaseId, int? installmentNumber) =>
        new()
        {
            UserId = userId,
            Source = TransactionSource.Manual,
            AccountId = card.Id,
            Type = TransactionType.Expense,
            Method = PaymentMethod.Credit,
            AmountCents = amountCents,
            PurchaseDate = purchaseDate,
            StatementId = statement.Id,
            SettlementDate = statement.DueDate, // CLAUDE.md, regra 4: no cartão, caixa = vencimento.
            CategoryId = category?.Id,
            Description = description,
            InstallmentPurchaseId = installmentPurchaseId,
            InstallmentNumber = installmentNumber,
        };

    // A categoria pode ter sido excluída enquanto a transação estava excluída: nesse caso a
    // transação volta sem categoria, em vez de ficar impossível de restaurar.
    public void Restore(bool categoryStillExists)
    {
        ClearDeletion();
        if (!categoryStillExists)
            CategoryId = null;
    }

    private void ApplySimple(
        Account account, TransactionType type, long amountCents, DateOnly purchaseDate,
        Category? category, PaymentMethod method, string? description)
    {
        AccountId = account.Id;
        Type = type;
        AmountCents = amountCents;
        PurchaseDate = purchaseDate;
        SettlementDate = purchaseDate; // CLAUDE.md, regra 4: fora do cartão, caixa = compra.
        CategoryId = category?.Id;
        Method = method;
        Description = description?.Trim() ?? "";
    }

    private static Error? ValidateSimple(
        Account account, TransactionType type, long amountCents, Category? category,
        PaymentMethod method, string? description)
    {
        if (type == TransactionType.Transfer)
            return Invalid("Transferência entre contas usa a operação de transferência.");

        if (account.Type == AccountType.CreditCard)
            return Invalid("Lançamento em cartão de crédito usa a compra no cartão.");

        if (method == PaymentMethod.Credit)
            return Invalid("Pagamento no crédito exige uma conta de cartão de crédito.");

        if (amountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (category is not null && category.Type != type)
            return Invalid("A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

        if (description?.Trim().Length > DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {DescriptionMaxLength} caracteres.");

        return null;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
