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
    // Mudar a data leva cada parcela para a fatura do seu ciclo a partir da nova data (etapa 1.14b).
    // installments: parcelas ativas da compra. statements: faturas do cartão (as das parcelas e as
    // que podem receber parcelas, inclusive em volta da nova data).
    public static Result<CardPurchaseEditResult> Edit(
        InstallmentPurchase purchase, Account card, IReadOnlyList<Transaction> installments,
        IReadOnlyCollection<Statement> statements, long totalAmountCents, int installmentCount,
        Category? category, string? description, DateOnly purchaseDate)
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

        var dateChanges = purchaseDate != purchase.PurchaseDate;
        if (dateChanges && paid.Count > 0)
            return PaidPurchaseKeepsItsDate;

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

        var placement = new StatementPlacement(card, statements, purchaseDate);

        // Com data nova, todas as parcelas mudam de fatura; nenhuma pode cair em fatura paga.
        // Sem data nova, só as parcelas acrescentadas precisam de fatura.
        var firstPlaced = dateChanges ? 1 : ordered.Count + 1;
        var targets = Enumerable.Range(firstPlaced, Math.Max(installmentCount - firstPlaced + 1, 0))
            .ToDictionary(n => n, placement.For);
        if (targets.Values.Any(t => t.IsPaid))
            return dateChanges ? MovedIntoPaidStatement : NewInstallmentsIntoPaidStatement;

        // Validado: a partir daqui nada falha.
        var text = description?.Trim() ?? "";
        var removed = unpaid.Where(t => t.InstallmentNumber > installmentCount).ToList();
        var kept = unpaid.Where(t => t.InstallmentNumber <= installmentCount).ToList();

        if (dateChanges)
            foreach (var installment in kept)
                installment.MoveTo(targets[installment.InstallmentNumber!.Value], purchaseDate);

        var added = new List<Transaction>();
        for (var number = ordered.Count + 1; number <= installmentCount; number++)
            added.Add(Transaction.CreateCardInstallment(
                purchase.UserId, card, 1, purchaseDate, targets[number], category, text, purchase.Id, number));

        var parts = new Money(remaining).SplitInto(Math.Max(unpaidCount, 1));
        var toDistribute = kept.Concat(added).OrderBy(t => t.InstallmentNumber).ToList();
        for (var i = 0; i < toDistribute.Count; i++)
        {
            toDistribute[i].Redistribute(parts[i].Cents);
            toDistribute[i].ApplyDetails(category, text);
        }

        purchase.Update(totalAmountCents, installmentCount, text, purchaseDate);
        return new CardPurchaseEditResult(added, removed, placement.Opened);
    }

    // PATCH /transactions/{id} de um lançamento no cartão (docs/fase-1.md, 2.2). A parcela isolada
    // muda só descrição e categoria; a compra à vista muda também valor e data, e a data nova a leva
    // para a fatura do seu ciclo. Devolve as faturas abertas para recebê-la.
    public static Result<IReadOnlyList<Statement>> EditTransaction(
        Transaction transaction, Account card, IReadOnlyCollection<Statement> statements,
        Account account, TransactionType type, long amountCents, DateOnly purchaseDate,
        Category? category, PaymentMethod method, string? description)
    {
        if (transaction.TransferPairId is not null)
            return Transaction.TransferIsNotEdited;

        if (transaction.StatementId is null)
            return Invalid("Lançamento fora do cartão usa a edição simples.");

        if (transaction.Type == TransactionType.Refund)
            return Invalid("Estorno usa a edição de estorno.");

        if (account.Id != transaction.AccountId || type != transaction.Type || method != transaction.Method)
            return Invalid("Em compra no cartão, conta, tipo e meio de pagamento não mudam. Exclua e lance de novo.");

        var isInstallment = transaction.InstallmentPurchaseId is not null;
        if (isInstallment && amountCents != transaction.AmountCents)
            return Invalid("O valor de uma parcela muda pela compra inteira, para a soma continuar igual ao total.");

        if (isInstallment && purchaseDate != transaction.PurchaseDate)
            return Invalid("A data de uma parcela muda pela compra inteira.");

        if (amountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        var inPaidStatement = statements.Any(s => s.Id == transaction.StatementId && s.IsPaid);
        if (inPaidStatement && amountCents != transaction.AmountCents)
            return Invalid("Esta compra está numa fatura paga; o valor não muda. Desfaça o pagamento para alterá-lo.");

        if (Transaction.ValidateDetails(type, category, description) is { } detailsError)
            return detailsError;

        var placement = new StatementPlacement(card, statements, purchaseDate);
        Statement? target = null;
        if (purchaseDate != transaction.PurchaseDate)
        {
            if (inPaidStatement)
                return PaidPurchaseKeepsItsDate;

            target = placement.For(1);
            if (target.IsPaid)
                return MovedIntoPaidStatement;
        }

        // Validado: a partir daqui nada falha.
        if (target is not null)
            transaction.MoveTo(target, purchaseDate);
        transaction.Redistribute(amountCents);
        transaction.ApplyDetails(category, description?.Trim() ?? "");
        return Result<IReadOnlyList<Statement>>.Success(placement.Opened);
    }

    private static readonly Error PaidPurchaseKeepsItsDate =
        new(ErrorType.Validation, "Há parcelas em fatura paga; a data da compra não pode mudar.");

    private static readonly Error MovedIntoPaidStatement =
        new(ErrorType.Validation, "A nova data leva parcelas para uma fatura já paga.");

    private static readonly Error NewInstallmentsIntoPaidStatement =
        new(ErrorType.Validation, "As novas parcelas cairiam numa fatura já paga. Desfaça o pagamento para aumentar as parcelas.");

    private static readonly Error PurchaseIntoPaidStatement =
        new(ErrorType.Validation, "Esta compra cai numa fatura já paga. Desfaça o pagamento para lançá-la.");

    // Fatura paga não muda de valor (docs/fase-1.md, 2.3): compra com parcela nela não sai (excluir)
    // nem volta (restaurar). installments: as transações da compra; statements: as faturas delas.
    public static Error? CheckCanRemove(IEnumerable<Transaction> installments, IReadOnlyCollection<Statement> statements) =>
        TouchesPaidStatement(installments, statements)
            ? new Error(ErrorType.Conflict, "Esta compra está numa fatura paga. Desfaça o pagamento para excluí-la.")
            : null;

    public static Error? CheckCanRestore(IEnumerable<Transaction> installments, IReadOnlyCollection<Statement> statements) =>
        TouchesPaidStatement(installments, statements)
            ? new Error(ErrorType.Conflict, "A fatura desta compra já está paga. Desfaça o pagamento para restaurá-la.")
            : null;

    private static bool TouchesPaidStatement(IEnumerable<Transaction> installments, IReadOnlyCollection<Statement> statements)
    {
        var paid = statements.Where(s => s.IsPaid).Select(s => s.Id).ToHashSet();
        return installments.Any(t => t.StatementId is { } id && paid.Contains(id));
    }

    // Fatura de cada parcela a partir de uma data de compra: reaproveita as faturas gravadas (e as
    // datas editadas delas) e abre as que faltam, como na criação.
    internal sealed class StatementPlacement(Account card, IReadOnlyCollection<Statement> statements, DateOnly purchaseDate)
    {
        private readonly Dictionary<string, Statement> _byReference = statements.ToDictionary(s => s.Reference);
        private readonly List<StatementDates> _existingDates = statements.Select(s => s.Dates).ToList();
        private readonly List<Statement> _opened = [];

        public IReadOnlyList<Statement> Opened => _opened;

        public Statement For(int installmentNumber)
        {
            var dates = StatementCalculator.ForInstallment(
                purchaseDate, installmentNumber, card.ClosingDay!.Value, card.DueDay!.Value, _existingDates);

            if (!_byReference.TryGetValue(dates.Reference, out var statement))
            {
                statement = Statement.Open(card.UserId, card.Id, dates);
                _byReference.Add(dates.Reference, statement);
                _opened.Add(statement);
            }
            return statement;
        }
    }

    // Desfaz a exclusão da compra (etapa 1.14). installments: as parcelas excluídas junto com ela.
    // Só restaura se elas forem exatamente as parcelas 1..N e somarem o total, para a soma nunca
    // divergir (CLAUDE.md, regra 2). Parcela cuja categoria foi excluída volta sem categoria.
    public static Result<InstallmentPurchase> Restore(
        InstallmentPurchase purchase, IReadOnlyList<Transaction> installments, IReadOnlySet<Guid> existingCategoryIds,
        IReadOnlyCollection<Statement> statements)
    {
        var numbers = installments.Select(t => t.InstallmentNumber).Order().ToList();
        var matches = installments.All(t => t.InstallmentPurchaseId == purchase.Id)
            && numbers.SequenceEqual(Enumerable.Range(1, purchase.InstallmentCount).Select(n => (int?)n))
            && installments.Sum(t => t.AmountCents) == purchase.TotalAmountCents;

        if (!matches)
            return new Error(ErrorType.Conflict,
                "As parcelas desta compra não fecham com o total; não é possível restaurá-la.");

        if (CheckCanRestore(installments, statements) is { } paidError)
            return paidError;

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

            if (statement.IsPaid)
                return PurchaseIntoPaidStatement;

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
