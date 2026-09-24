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
public sealed record CardPurchaseEditResult(
    IReadOnlyList<Transaction> Added,
    IReadOnlyList<Transaction> Removed,
    IReadOnlyList<Statement> OpenedStatements);

public static class CardPurchase
{
    // Editar a compra redistribui as parcelas não pagas e mantém a soma exata (docs/fase-1.md,
    // 2.2). Parcela paga é a que está em fatura paga: mantém o valor e não pode ser removida.
    // installments: parcelas ativas da compra. statements: faturas do cartão (as das parcelas e as
    // que podem receber parcelas novas).
    public static Result<CardPurchaseEditResult> Edit(
        InstallmentPurchase purchase, Account card, IReadOnlyList<Transaction> installments,
        IReadOnlyCollection<Statement> statements, long totalAmountCents, int installmentCount,
        Category? category, string? description)
    {
        if (installmentCount is < 1 or > InstallmentPurchase.MaxInstallments)
            return Invalid($"O número de parcelas deve estar entre 1 e {InstallmentPurchase.MaxInstallments}.");

        if (totalAmountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (Transaction.ValidateDetails(TransactionType.Expense, category, description) is { } detailsError)
            return detailsError;

        var statementsById = statements.ToDictionary(s => s.Id);
        bool IsPaid(Transaction t) => statementsById.TryGetValue(t.StatementId!.Value, out var s) && s.IsPaid;

        var ordered = installments.OrderBy(t => t.InstallmentNumber).ToList();
        var paid = ordered.Where(IsPaid).ToList();
        var unpaid = ordered.Where(t => !IsPaid(t)).ToList();

        if (paid.Count > installmentCount)
            return Invalid($"Já há {paid.Count} parcelas pagas; a compra não pode ter menos que isso.");

        if (paid.Any(t => t.InstallmentNumber > installmentCount))
            return Invalid("Parcelas já pagas não podem ser removidas.");

        var unpaidCount = installmentCount - paid.Count;
        var remaining = totalAmountCents - paid.Sum(t => t.AmountCents);

        if (unpaidCount == 0 && remaining != 0)
            return Invalid("Todas as parcelas estão pagas; o valor total não pode mudar.");

        if (unpaidCount > 0 && remaining < unpaidCount)
            return Invalid("O valor total deve ter ao menos 1 centavo por parcela não paga.");

        // Validado: a partir daqui nada falha.
        var text = description?.Trim() ?? "";
        var removed = unpaid.Where(t => t.InstallmentNumber > installmentCount).ToList();
        var kept = unpaid.Where(t => t.InstallmentNumber <= installmentCount).ToList();

        var byReference = statements.ToDictionary(s => s.Reference);
        var existingDates = statements.Select(s => s.Dates).ToList();
        var opened = new List<Statement>();
        var added = new List<Transaction>();

        for (var number = ordered.Count + 1; number <= installmentCount; number++)
        {
            var dates = StatementCalculator.ForInstallment(
                purchase.PurchaseDate, number, card.ClosingDay!.Value, card.DueDay!.Value, existingDates);

            if (!byReference.TryGetValue(dates.Reference, out var statement))
            {
                statement = Statement.Open(purchase.UserId, card.Id, dates);
                byReference.Add(dates.Reference, statement);
                opened.Add(statement);
            }

            added.Add(Transaction.CreateCardInstallment(
                purchase.UserId, card, 1, purchase.PurchaseDate, statement, category, text, purchase.Id, number));
        }

        var parts = new Money(remaining).SplitInto(Math.Max(unpaidCount, 1));
        var toDistribute = kept.Concat(added).OrderBy(t => t.InstallmentNumber).ToList();
        for (var i = 0; i < toDistribute.Count; i++)
        {
            toDistribute[i].Redistribute(parts[i].Cents);
            toDistribute[i].ApplyDetails(category, text);
        }

        purchase.Update(totalAmountCents, installmentCount, text);
        return new CardPurchaseEditResult(added, removed, opened);
    }

    // Desfaz a exclusão da compra (etapa 1.14). installments: as parcelas excluídas junto com ela.
    // Só restaura se elas forem exatamente as parcelas 1..N e somarem o total, para a soma nunca
    // divergir (CLAUDE.md, regra 2). Parcela cuja categoria foi excluída volta sem categoria.
    public static Result<InstallmentPurchase> Restore(
        InstallmentPurchase purchase, IReadOnlyList<Transaction> installments, IReadOnlySet<Guid> existingCategoryIds)
    {
        var numbers = installments.Select(t => t.InstallmentNumber).Order().ToList();
        var matches = installments.All(t => t.InstallmentPurchaseId == purchase.Id)
            && numbers.SequenceEqual(Enumerable.Range(1, purchase.InstallmentCount).Select(n => (int?)n))
            && installments.Sum(t => t.AmountCents) == purchase.TotalAmountCents;

        if (!matches)
            return new Error(ErrorType.Conflict,
                "As parcelas desta compra não fecham com o total; não é possível restaurá-la.");

        purchase.Restore();
        foreach (var installment in installments)
            installment.Restore(installment.CategoryId is not { } id || existingCategoryIds.Contains(id));

        return purchase;
    }

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
