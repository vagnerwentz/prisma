using Prisma.Domain.Accounts;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;

namespace Prisma.Domain.Transactions;

public sealed record CardPurchaseResult(
    InstallmentPurchase? Purchase,
    IReadOnlyList<Transaction> Installments,
    IReadOnlyList<Statement> OpenedStatements);

// Compra no cartão, à vista ou parcelada (docs/fase-1.md, 2.1 e 2.2). Cada parcela entra na
// fatura do seu ciclo e tem como SettlementDate o vencimento dessa fatura.
public static class CardPurchase
{
    public static readonly Error InstallmentsRequireCard =
        new(ErrorType.Validation, "Parcelamento só existe em cartão de crédito.");

    // existingStatements: faturas já gravadas deste cartão. São reaproveitadas, e as datas
    // delas (inclusive editadas) prevalecem sobre o cálculo.
    public static Result<CardPurchaseResult> Create(
        Guid userId, Account card, TransactionType type, PaymentMethod method, long totalAmountCents,
        int installmentCount, DateOnly purchaseDate, Category? category, string? description,
        IReadOnlyCollection<Statement> existingStatements)
    {
        if (Validate(card, type, method, totalAmountCents, installmentCount, category, description) is { } error)
            return error;

        var text = description?.Trim() ?? "";
        var parts = new Money(totalAmountCents).SplitInto(installmentCount);
        var purchase = installmentCount > 1
            ? InstallmentPurchase.Create(userId, card.Id, text, totalAmountCents, installmentCount, purchaseDate)
            : null;

        var statements = existingStatements.ToDictionary(s => s.Reference);
        var existingDates = existingStatements.Select(s => s.Dates).ToList();
        var opened = new List<Statement>();
        var installments = new List<Transaction>();

        for (var number = 1; number <= installmentCount; number++)
        {
            var dates = StatementCalculator.ForInstallment(
                purchaseDate, number, card.ClosingDay!.Value, card.DueDay!.Value, existingDates);

            if (!statements.TryGetValue(dates.Reference, out var statement))
            {
                statement = Statement.Open(userId, card.Id, dates);
                statements.Add(dates.Reference, statement);
                opened.Add(statement);
            }

            installments.Add(Transaction.CreateCardInstallment(
                userId, card, parts[number - 1].Cents, purchaseDate, statement, category, text,
                purchase?.Id, purchase is null ? null : number));
        }

        return new CardPurchaseResult(purchase, installments, opened);
    }

    private static Error? Validate(
        Account card, TransactionType type, PaymentMethod method, long totalAmountCents,
        int installmentCount, Category? category, string? description)
    {
        if (card.Type != AccountType.CreditCard)
            return Invalid("Compra no cartão exige uma conta de cartão de crédito.");

        if (type != TransactionType.Expense)
            return Invalid("O cartão aceita apenas despesas.");

        if (method != PaymentMethod.Credit)
            return Invalid("Compra no cartão usa o meio de pagamento crédito.");

        if (installmentCount is < 1 or > InstallmentPurchase.MaxInstallments)
            return Invalid($"O número de parcelas deve estar entre 1 e {InstallmentPurchase.MaxInstallments}.");

        if (totalAmountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (totalAmountCents < installmentCount)
            return Invalid("O valor total deve ter ao menos 1 centavo por parcela.");

        if (category is not null && category.Type != type)
            return Invalid("A categoria deve ser do mesmo tipo da transação (receita ou despesa).");

        if (description?.Trim().Length > Transaction.DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {Transaction.DescriptionMaxLength} caracteres.");

        return null;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
