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

// Resultado de mover uma compra de fatura: as transações movidas, as faturas abertas para recebê-las
// e se a compra ficou presa (não fica quando volta à fatura que a previsão daria).
public sealed record StatementMove(IReadOnlyList<Transaction> Moved, IReadOnlyList<Statement> Opened, bool Pinned);

public static class CardPurchase
{
    // Muda a compra de fatura sem mudar a data dela (docs/fase-2.md, 2.9, regras 2 e 3): à vista, a
    // transação; parcelada, todas as parcelas, cada uma um ciclo. purchase: as transações ativas da
    // compra; statements: todas as faturas do cartão.
    public static Result<StatementMove> MoveStatement(
        IReadOnlyList<Transaction> purchase, Account card, IReadOnlyCollection<Statement> statements, StatementShift shift)
    {
        if (purchase.Count == 0)
            throw new ArgumentException("A compra não tem transações.", nameof(purchase));

        var group = purchase[0].InstallmentPurchaseId;
        if (purchase.Any(t => t.InstallmentPurchaseId != group) || (group is null && purchase.Count > 1))
            throw new ArgumentException("As transações devem ser da mesma compra.", nameof(purchase));

        if (purchase.Any(t => t.Type == TransactionType.Refund))
            return Invalid("Estorno segue a data dele. Para mudá-lo de fatura, mude a data do estorno.");

        if (purchase.Any(t => t.StatementId is null || t.Type != TransactionType.Expense || t.AccountId != card.Id))
            return Invalid("Só compras no cartão mudam de fatura.");

        var byId = statements.ToDictionary(s => s.Id);
        var byReference = statements.ToDictionary(s => s.Reference);
        if (purchase.Any(t => byId[t.StatementId!.Value].IsPaid))
            return Invalid("Esta compra está numa fatura paga. Desfaça o pagamento para mudá-la de fatura.");

        var step = shift == StatementShift.Next ? 1 : -1;
        var targets = purchase.ToDictionary(t => t, t =>
        {
            var reference = StatementCalculator.ShiftReference(byId[t.StatementId!.Value].Reference, step);
            return byReference.TryGetValue(reference, out var existing)
                ? (Existing: existing, Dates: existing.Dates)
                : (Existing: (Statement?)null, Dates: StatementCalculator.ForReference(reference, card.ClosingDay!.Value, card.DueDay!.Value));
        });

        if (targets.Values.Any(t => t.Existing?.IsPaid == true))
            return Invalid(shift == StatementShift.Next ? "A fatura seguinte já está paga." : "A fatura anterior já está paga.");

        // Validado: a partir daqui nada falha.
        var opened = new List<Statement>();
        var destination = targets.ToDictionary(pair => pair.Key, pair =>
        {
            if (pair.Value.Existing is { } existing)
                return existing;
            var statement = Statement.Open(card.UserId, card.Id, pair.Value.Dates);
            opened.Add(statement);
            return statement;
        });

        // Regra 3: se a primeira parcela cai onde a previsão a poria, a compra volta a ser do cálculo.
        var first = purchase.OrderBy(t => t.InstallmentNumber ?? 1).First();
        var dates = statements.Select(s => s.Dates).Concat(opened.Select(s => s.Dates)).ToList();
        var calculated = StatementCalculator.ForInstallment(
            first.PurchaseDate, first.InstallmentNumber ?? 1, card.ClosingDay!.Value, card.DueDay!.Value, dates);
        var pinned = calculated.Reference != destination[first].Reference;

        foreach (var (transaction, statement) in destination)
            transaction.MoveToStatement(statement, pinned);

        return new StatementMove(purchase, opened, pinned);
    }

    // Editar a compra redistribui as parcelas não pagas e mantém a soma exata (docs/fase-1.md,
    // 2.2). Parcela paga é a que está em fatura paga: mantém o valor e não pode ser removida.
    // Mudar a data leva cada parcela para a fatura do seu ciclo a partir da nova data (etapa 1.14b);
    // trocar o cartão (newCard), para a do cartão novo, com a compra inteira (docs/fase-2.md, 2.13).
    // installments: parcelas ativas da compra. statements: faturas do cartão (as das parcelas e as
    // que podem receber parcelas, inclusive em volta da nova data), e as do cartão novo, na troca.
    public static Result<CardPurchaseEditResult> Edit(
        InstallmentPurchase purchase, Account card, IReadOnlyList<Transaction> installments,
        IReadOnlyCollection<Statement> statements, long totalAmountCents, int installmentCount,
        Category? category, string? description, DateOnly purchaseDate, Account? newCard = null)
    {
        var target = newCard ?? card;
        var cardChanges = target.Id != card.Id;
        if (cardChanges && target.Type != AccountType.CreditCard)
            return OnlyToAnotherCard;

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
        if (cardChanges && paid.Count > 0)
            return Invalid("Há parcelas em fatura paga. Desfaça o pagamento para trocar o cartão.");

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

        var placement = new StatementPlacement(target, statements, purchaseDate);

        // Com data ou cartão novos, todas as parcelas mudam de fatura; nenhuma pode cair em fatura paga.
        // Sem eles, só as parcelas acrescentadas precisam de fatura. Na compra presa (movida de
        // fatura, docs/fase-2.md, 2.9), elas seguem a última parcela, para os ciclos continuarem
        // consecutivos, e ficam presas como as outras.
        var relocates = dateChanges || cardChanges;
        var pinnedLast = !relocates && ordered.Any(t => t.StatementPinned) ? ordered[^1] : null;
        var firstPlaced = relocates ? 1 : ordered.Count + 1;
        var targets = Enumerable.Range(firstPlaced, Math.Max(installmentCount - firstPlaced + 1, 0))
            .ToDictionary(n => n, n => pinnedLast is not null
                ? placement.After(statementsById[pinnedLast.StatementId!.Value], n - pinnedLast.InstallmentNumber!.Value)
                : placement.For(n));
        if (targets.Values.Any(t => t.IsPaid))
            return cardChanges ? IntoPaidStatementOfNewCard
                : dateChanges ? MovedIntoPaidStatement
                : NewInstallmentsIntoPaidStatement;

        // Validado: a partir daqui nada falha.
        var text = description?.Trim() ?? "";
        var removed = unpaid.Where(t => t.InstallmentNumber > installmentCount).ToList();
        var kept = unpaid.Where(t => t.InstallmentNumber <= installmentCount).ToList();

        if (relocates)
            foreach (var installment in kept)
            {
                var statement = targets[installment.InstallmentNumber!.Value];
                if (cardChanges)
                    installment.MoveToCard(target, statement, purchaseDate);
                else
                    installment.MoveTo(statement, purchaseDate);
            }

        var added = new List<Transaction>();
        for (var number = ordered.Count + 1; number <= installmentCount; number++)
        {
            var installment = Transaction.CreateCardInstallment(
                purchase.UserId, target, 1, purchaseDate, targets[number], category, text, purchase.Id, number);
            if (pinnedLast is not null)
                installment.MoveToStatement(targets[number], pinned: true);
            added.Add(installment);
        }

        var parts = new Money(remaining).SplitInto(Math.Max(unpaidCount, 1));
        var toDistribute = kept.Concat(added).OrderBy(t => t.InstallmentNumber).ToList();
        for (var i = 0; i < toDistribute.Count; i++)
        {
            toDistribute[i].Redistribute(parts[i].Cents);
            toDistribute[i].ApplyDetails(category, text);
        }

        purchase.Update(totalAmountCents, installmentCount, text, purchaseDate);
        if (cardChanges)
            purchase.MoveToCard(target.Id);
        return new CardPurchaseEditResult(added, removed, placement.Opened);
    }

    // PATCH /transactions/{id} de um lançamento no cartão (docs/fase-1.md, 2.2). A parcela isolada
    // muda só descrição e categoria; a compra à vista muda também valor, data e cartão, e a data ou o
    // cartão novos a levam para a fatura do seu ciclo (docs/fase-2.md, 2.13). Devolve as faturas
    // abertas para recebê-la. statements: as faturas do cartão (e as do cartão novo, na troca).
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

        if (type != transaction.Type || method != transaction.Method)
            return TypeAndMethodKeep;

        var isInstallment = transaction.InstallmentPurchaseId is not null;
        var cardChanges = account.Id != transaction.AccountId;
        if (cardChanges && account.Type != AccountType.CreditCard)
            return OnlyToAnotherCard;

        if (isInstallment && cardChanges)
            return Invalid("O cartão de uma parcela muda pela compra inteira.");

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

        var placement = new StatementPlacement(account, statements, purchaseDate);
        Statement? target = null;
        if (cardChanges || purchaseDate != transaction.PurchaseDate)
        {
            if (inPaidStatement)
                return cardChanges ? Invalid("Esta compra está numa fatura paga. Desfaça o pagamento para trocar o cartão.")
                    : PaidPurchaseKeepsItsDate;

            target = placement.For(1);
            if (target.IsPaid)
                return cardChanges ? IntoPaidStatementOfNewCard : MovedIntoPaidStatement;
        }

        // Validado: a partir daqui nada falha.
        if (target is not null && cardChanges)
            transaction.MoveToCard(account, target, purchaseDate);
        else if (target is not null)
            transaction.MoveTo(target, purchaseDate);
        transaction.Redistribute(amountCents);
        transaction.ApplyDetails(category, description?.Trim() ?? "");
        return Result<IReadOnlyList<Statement>>.Success(placement.Opened);
    }

    private static readonly Error TypeAndMethodKeep =
        new(ErrorType.Validation, "Em compra no cartão, tipo e meio de pagamento não mudam. Exclua e lance de novo.");

    private static readonly Error OnlyToAnotherCard =
        new(ErrorType.Validation, "Compra no cartão só troca para outro cartão. Para usar outra conta, exclua e lance de novo.");

    private static readonly Error IntoPaidStatementOfNewCard =
        new(ErrorType.Validation, "No cartão novo, a compra cairia numa fatura já paga.");

    private static readonly Error PaidPurchaseKeepsItsDate =
        new(ErrorType.Validation, "Há parcelas em fatura paga; a data da compra não pode mudar.");

    private static readonly Error MovedIntoPaidStatement =
        new(ErrorType.Validation, "A nova data leva parcelas para uma fatura já paga.");

    private static readonly Error NewInstallmentsIntoPaidStatement =
        new(ErrorType.Validation, "As novas parcelas cairiam numa fatura já paga. Desfaça o pagamento para aumentar as parcelas.");

    // Internal: a série que se repete reconhece esta recusa e a transforma em pendência (docs/fase-2.md, 2.14, regra 9).
    internal static readonly Error PurchaseIntoPaidStatement =
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
    // datas editadas delas) e abre as que faltam, como na criação. Só olha as faturas do cartão: na
    // troca de cartão, as dos dois vêm juntas, e a mesma referência existe nos dois.
    internal sealed class StatementPlacement(Account card, IReadOnlyCollection<Statement> statements, DateOnly purchaseDate)
    {
        private readonly Dictionary<string, Statement> _byReference =
            statements.Where(s => s.AccountId == card.Id).ToDictionary(s => s.Reference);
        private readonly List<StatementDates> _existingDates =
            statements.Where(s => s.AccountId == card.Id).Select(s => s.Dates).ToList();
        private readonly List<Statement> _opened = [];

        public IReadOnlyList<Statement> Opened => _opened;

        public Statement For(int installmentNumber)
        {
            var dates = StatementCalculator.ForInstallment(
                purchaseDate, installmentNumber, card.ClosingDay!.Value, card.DueDay!.Value, _existingDates);
            return Existing(dates.Reference) ?? Open(dates);
        }

        // A fatura "months" ciclos depois de outra, aberta com as datas calculadas se faltar.
        public Statement After(Statement statement, int months)
        {
            var reference = StatementCalculator.ShiftReference(statement.Reference, months);
            return Existing(reference)
                   ?? Open(StatementCalculator.ForReference(reference, card.ClosingDay!.Value, card.DueDay!.Value));
        }

        private Statement? Existing(string reference) => _byReference.GetValueOrDefault(reference);

        private Statement Open(StatementDates dates)
        {
            var statement = Statement.Open(card.UserId, card.Id, dates);
            _byReference.Add(dates.Reference, statement);
            _opened.Add(statement);
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

        // A mesma escolha de fatura da edição e do estorno (e do recálculo, StatementReconciliation).
        var placement = new StatementPlacement(card, existingStatements, purchaseDate);
        var installments = new List<Transaction>();

        for (var number = 1; number <= installmentCount; number++)
        {
            var statement = placement.For(number);
            if (statement.IsPaid)
                return PurchaseIntoPaidStatement;

            installments.Add(Transaction.CreateCardInstallment(
                userId, card, parts[number - 1].Cents, purchaseDate, statement, category, text,
                purchase?.Id, purchase is null ? null : number));
        }

        return new CardPurchaseResult(purchase, installments, placement.Opened);
    }

    // Compra que cairia numa fatura paga, lançada na seguinte e presa a ela, como no "Mover para"
    // (docs/fase-2.md, 2.14, regra 9; 2.9, regra 2). A data da compra não muda.
    internal static Result<CardPurchaseResult> CreateInNextStatement(
        Guid userId, Account card, long amountCents, DateOnly purchaseDate, Category? category, string? description,
        IReadOnlyCollection<Statement> existingStatements)
    {
        if (Validate(card, TransactionType.Expense, PaymentMethod.Credit, amountCents, 1, category, description) is { } error)
            return error;

        var placement = new StatementPlacement(card, existingStatements, purchaseDate);
        var next = placement.After(placement.For(1), 1);
        if (next.IsPaid)
            return Invalid("A fatura seguinte também já está paga.");

        var charge = Transaction.CreateCardInstallment(
            userId, card, amountCents, purchaseDate, next, category, description?.Trim() ?? "", null, null);
        charge.MoveToStatement(next, pinned: true);
        return new CardPurchaseResult(null, [charge], placement.Opened);
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
