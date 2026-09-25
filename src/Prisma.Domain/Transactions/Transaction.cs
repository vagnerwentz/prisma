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
    public TransferDirection? TransferDirection { get; private set; }
    public TransactionSource Source { get; private init; }

    // Estorno ligado à compra que ele devolve (docs/fase-2.md, 2.5, regra 7).
    public Guid? RefundedTransactionId { get; private init; }

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

    internal static Error? ValidateDetails(TransactionType type, Category? category, string? description)
    {
        if (category is not null && category.Type != type)
            return Invalid("A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

        if (description?.Trim().Length > DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {DescriptionMaxLength} caracteres.");

        return null;
    }

    internal void ApplyDetails(Category? category, string description)
    {
        CategoryId = category?.Id;
        Description = description;
    }

    // Usado por CardPurchase, que valida o valor (e, na compra parcelada, garante a soma exata).
    internal void Redistribute(long amountCents) => AmountCents = amountCents;

    // Usado por CardPurchase ao mudar a data da compra: a transação passa para a fatura do novo
    // ciclo e o caixa segue o vencimento dela (CLAUDE.md, regra 4).
    internal void MoveTo(Statement statement, DateOnly purchaseDate)
    {
        PurchaseDate = purchaseDate;
        StatementId = statement.Id;
        SettlementDate = statement.DueDate;
    }

    internal void SettleOn(Statement statement)
    {
        if (StatementId != statement.Id)
            throw new ArgumentException("A transação não pertence a esta fatura.", nameof(statement));

        SettlementDate = statement.DueDate;
    }

    public Result<Transaction> UpdateSimple(
        Account account, TransactionType type, long amountCents, DateOnly purchaseDate,
        Category? category, PaymentMethod method, string? description)
    {
        if (TransferPairId is not null)
            return TransferIsNotEdited;

        if (StatementId is not null)
            return Invalid("Lançamento em cartão de crédito usa a compra no cartão.");

        if (Type == TransactionType.Refund)
            return Invalid("Estorno usa a edição de estorno.");

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

    // Criado só por Refund, que valida antes. Sem fatura, o caixa é a própria data; no cartão, o
    // vencimento da fatura aberta na data do estorno (docs/fase-2.md, 2.5, regra 4).
    internal static Transaction CreateRefund(
        Guid userId, Account account, long amountCents, DateOnly date, Statement? statement,
        Category? category, PaymentMethod method, string description, Guid? refundedTransactionId) =>
        new()
        {
            UserId = userId,
            Source = TransactionSource.Manual,
            AccountId = account.Id,
            Type = TransactionType.Refund,
            Method = method,
            AmountCents = amountCents,
            PurchaseDate = date,
            StatementId = statement?.Id,
            SettlementDate = statement?.DueDate ?? date,
            CategoryId = category?.Id,
            Description = description,
            RefundedTransactionId = refundedTransactionId,
        };

    // Edição do estorno, validada por Refund.
    internal void ApplyRefund(long amountCents, DateOnly date, Statement? statement, Category? category, PaymentMethod method, string description)
    {
        AmountCents = amountCents;
        PurchaseDate = date;
        StatementId = statement?.Id;
        SettlementDate = statement?.DueDate ?? date;
        CategoryId = category?.Id;
        Method = method;
        Description = description;
    }

    public static readonly Error TransferIsNotEdited =
        new(ErrorType.Validation, "Transferência não é editada. Exclua e lance de novo.");

    // Ponta de uma transferência, criada só por Transfer. Na entrada de um pagamento de fatura,
    // o StatementId liga a transferência à fatura paga; o caixa é a data do pagamento.
    internal static Transaction CreateTransferLeg(
        Guid userId, Account account, TransferDirection direction, Guid pairId, long amountCents, DateOnly date,
        PaymentMethod method, string description, Statement? paidStatement) =>
        new()
        {
            UserId = userId,
            Source = TransactionSource.Manual,
            AccountId = account.Id,
            Type = TransactionType.Transfer,
            Method = method,
            AmountCents = amountCents,
            PurchaseDate = date,
            SettlementDate = date,
            StatementId = paidStatement?.Id,
            Description = description,
            TransferPairId = pairId,
            TransferDirection = direction,
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

        if (type == TransactionType.Refund)
            return Invalid("Estorno usa o lançamento de estorno.");

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
