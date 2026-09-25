using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Transactions;

// A compra que o estorno ligado devolve e quanto ainda pode ser estornado dela: o valor da compra
// (na parcelada, o total) menos os outros estornos ligados a ela.
public sealed record RefundTarget(Transaction Purchase, long RefundableCents);

public sealed record RefundResult(Transaction Refund, IReadOnlyList<Statement> OpenedStatements);

// Estorno (docs/fase-2.md, 2.5): dinheiro que volta de uma compra. Abate despesa, nunca é receita.
// No cartão, entra na fatura aberta na data do estorno, não na da compra original.
public static class Refund
{
    public static long RefundableCents(long purchaseCents, long alreadyRefundedCents) =>
        purchaseCents - alreadyRefundedCents;

    // statements: as faturas do cartão em volta da data (ignoradas fora do cartão).
    public static Result<RefundResult> Create(
        Guid userId, Account account, long amountCents, DateOnly date, Category? category, PaymentMethod method,
        string? description, RefundTarget? target, IReadOnlyCollection<Statement> statements)
    {
        if (Validate(account, amountCents, category, method, description) is { } error)
            return error;

        if (target is not null)
        {
            if (target.Purchase.Type != TransactionType.Expense)
                return Invalid("Só uma despesa pode ser estornada.");

            if (target.Purchase.AccountId != account.Id)
                return Invalid("O estorno fica na mesma conta da compra.");

            if (CheckLimit(amountCents, target.RefundableCents) is { } limitError)
                return limitError;
        }

        Statement? statement = null;
        IReadOnlyList<Statement> opened = [];
        if (account.Type == AccountType.CreditCard)
        {
            var placement = new CardPurchase.StatementPlacement(account, statements, date);
            statement = placement.For(1);
            if (statement.IsPaid)
                return IntoPaidStatement;
            opened = placement.Opened;
        }

        var refund = Transaction.CreateRefund(
            userId, account, amountCents, date, statement, category, method, description?.Trim() ?? "", target?.Purchase.Id);
        return new RefundResult(refund, opened);
    }

    // Valor, data, categoria, descrição e meio mudam; conta e vínculo, não (regra 14).
    // refundableCents: quanto a compra ligada ainda aceita, sem contar este estorno; null se avulso.
    public static Result<IReadOnlyList<Statement>> Edit(
        Transaction refund, Account account, long amountCents, DateOnly date, Category? category, PaymentMethod method,
        string? description, long? refundableCents, IReadOnlyCollection<Statement> statements)
    {
        if (refund.Type != TransactionType.Refund)
            throw new ArgumentException("A transação não é um estorno.", nameof(refund));

        if (account.Id != refund.AccountId)
            return Invalid("No estorno, a conta não muda. Exclua e lance de novo.");

        if (Validate(account, amountCents, category, method, description) is { } error)
            return error;

        if (refundableCents is { } refundable && CheckLimit(amountCents, refundable) is { } limitError)
            return limitError;

        Statement? statement = null;
        IReadOnlyList<Statement> opened = [];
        if (account.Type == AccountType.CreditCard)
        {
            var current = statements.Single(s => s.Id == refund.StatementId);
            var changes = amountCents != refund.AmountCents || date != refund.PurchaseDate;
            if (current.IsPaid && changes)
                return Invalid("Este estorno está numa fatura paga; valor e data não mudam. Desfaça o pagamento para alterá-los.");

            statement = current;
            if (date != refund.PurchaseDate)
            {
                var placement = new CardPurchase.StatementPlacement(account, statements, date);
                statement = placement.For(1);
                if (statement.IsPaid)
                    return Invalid("A nova data leva o estorno para uma fatura já paga.");
                opened = placement.Opened;
            }
        }

        refund.ApplyRefund(amountCents, date, statement, category, method, description?.Trim() ?? "");
        return Result<IReadOnlyList<Statement>>.Success(opened);
    }

    // Restaurar segue as regras de criar (regra 15). statements: a fatura do estorno, se no cartão.
    public static Error? CheckCanRestore(Transaction refund, long? refundableCents, IReadOnlyCollection<Statement> statements)
    {
        if (refund.StatementId is { } statementId && statements.Any(s => s.Id == statementId && s.IsPaid))
            return new Error(ErrorType.Conflict, "A fatura deste estorno já está paga. Desfaça o pagamento para restaurá-lo.");

        return refundableCents is { } refundable ? CheckLimit(refund.AmountCents, refundable) : null;
    }

    // Compra com estorno não muda de tipo nem de conta, e não fica menor do que já foi estornado.
    public static Error? CheckPurchaseEdit(
        Transaction purchase, TransactionType newType, Guid newAccountId, long newAmountCents, long refundedCents)
    {
        if (refundedCents <= 0)
            return null;

        if (newType != purchase.Type || newAccountId != purchase.AccountId)
            return Invalid("Esta despesa tem estornos; o tipo e a conta não mudam.");

        return CheckPurchaseKeepsRefunds(newAmountCents, refundedCents);
    }

    // A compra não pode ficar menor do que já foi estornado dela.
    public static Error? CheckPurchaseKeepsRefunds(long newAmountCents, long refundedCents) =>
        newAmountCents < refundedCents
            ? Invalid($"Já foram estornados {new Money(refundedCents)} desta compra; o valor não pode ficar abaixo disso.")
            : null;

    private static Error? CheckLimit(long amountCents, long refundableCents) =>
        refundableCents <= 0 ? Invalid("Esta compra já foi estornada por inteiro.")
        : amountCents > refundableCents ? Invalid($"O estorno passa do valor da compra. Restam {new Money(refundableCents)} para estornar.")
        : null;

    private static Error? Validate(Account account, long amountCents, Category? category, PaymentMethod method, string? description)
    {
        if (account.Type == AccountType.Investment)
            return Invalid("Estorno vai para a conta ou o cartão em que a compra foi feita.");

        if (account.Type == AccountType.CreditCard && method != PaymentMethod.Credit)
            return Invalid("No cartão, o estorno usa o meio de pagamento crédito.");

        if (account.Type != AccountType.CreditCard && method == PaymentMethod.Credit)
            return Invalid("Pagamento no crédito exige uma conta de cartão de crédito.");

        if (amountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (category is not null && category.Type != TransactionType.Expense)
            return Invalid("A categoria do estorno deve ser de despesa.");

        if (description?.Trim().Length > Transaction.DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {Transaction.DescriptionMaxLength} caracteres.");

        return null;
    }

    private static readonly Error IntoPaidStatement =
        Invalid("Esta fatura já está paga. Lance o estorno com a data em que ele apareceu no cartão.");

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
