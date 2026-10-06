using Prisma.Domain.Accounts;
using Prisma.Domain.Calendar;
using Prisma.Domain.Categories;
using Prisma.Domain.Statements;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Recurrences;

// Ocorrência que cairia numa fatura já paga: não é lançada e espera a pessoa decidir
// (docs/fase-2.md, 2.14, regra 9).
public sealed record PendingOccurrence(DateOnly OccurrenceDate, string StatementReference);

// O que uma geração fez: os lançamentos novos, as faturas abertas para recebê-los e as pendências.
public sealed record RecurrenceGeneration(
    IReadOnlyList<Transaction> Created, IReadOnlyList<Statement> Opened, IReadOnlyList<PendingOccurrence> Pending)
{
    public static readonly RecurrenceGeneration Nothing = new([], [], []);
}

// Lançamento que se repete (docs/fase-2.md, 2.14). Guarda o modelo do lançamento e a agenda; cada
// ocorrência vira lançamento de verdade só no dia dela, pelas fábricas de sempre: fora do cartão, um
// lançamento simples; no cartão, uma compra à vista, na fatura que a regra do cartão der. Nada futuro é
// gravado. GeneratedThrough é até onde a série já foi gerada: gerar de novo não repete nada.
public sealed class Recurrence : Entity
{
    private Recurrence() { }

    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public long AmountCents { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Description { get; private set; } = "";
    public PaymentMethod Method { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }

    // A partida da agenda: a primeira ocorrência, ou a nova próxima data quando a frequência muda.
    public DateOnly StartDate { get; private set; }

    // Término, inclusive; sem término, a série segue até ser encerrada.
    public DateOnly? EndDate { get; private set; }

    public DateOnly GeneratedThrough { get; private set; }

    // Débito automático (docs/fase-2.md, 2.15): a agenda dá o vencimento, e o lançamento sai no próximo dia
    // útil. AmountVaries: o valor é a estimativa, e cada ocorrência sai a conferir.
    public RecurrenceKind Kind { get; private set; }
    public bool AmountVaries { get; private set; }

    public AutoDebitTerms? AutoDebit => Kind == RecurrenceKind.AutoDebit ? new AutoDebitTerms(AmountVaries) : null;

    // No cartão, a série é de compras; o crédito só existe no cartão.
    private bool OnCard => Method == PaymentMethod.Credit;

    public DateOnly? NextOccurrence => RecurrenceSchedule.Next(StartDate, Frequency, EndDate, GeneratedThrough);

    // A data que o próximo lançamento vai ter: no débito automático, a do débito.
    public DateOnly? NextTransactionDate => NextOccurrence is { } next ? TransactionDateOf(next) : null;

    // A data do lançamento de uma ocorrência (2.15, regra 3): no débito automático, o próximo dia útil a
    // partir do vencimento; nas outras, a própria data da agenda (2.14, regra 3).
    public DateOnly TransactionDateOf(DateOnly occurrence) =>
        Kind == RecurrenceKind.AutoDebit ? BankCalendar.BusinessDayOnOrAfter(occurrence) : occurrence;

    public const int FirstDueDateMaxDaysBefore = 7;

    // A série nasce de um lançamento, que vira a primeira ocorrência (regra 1): nada é gerado para trás.
    // autoDebit: a série é de débito automático (docs/fase-2.md, 2.15); o vencimento da primeira ocorrência
    // pode ser até 7 dias antes da data do lançamento, quando o débito já caiu adiado (regra 4).
    public static Result<Recurrence> StartFrom(
        Transaction first, Account account, RecurrenceFrequency frequency, DateOnly? endDate, AutoDebitTerms? autoDebit = null)
    {
        if (first.AccountId != account.Id)
            throw new ArgumentException("A conta deve ser a do lançamento.", nameof(account));

        if (first.RecurrenceId is not null)
            return new Error(ErrorType.Conflict, "Este lançamento já faz parte de uma série.");

        if (first.Type == TransactionType.Transfer)
            return Invalid("Transferência entre contas não se repete.");

        if (first.Type == TransactionType.Refund)
            return Invalid("Estorno não se repete.");

        if (first.InstallmentPurchaseId is not null)
            return Invalid("Compra parcelada não se repete.");

        // O valor do provento muda a cada pagamento (docs/investimentos.md, etapa 5b).
        if (first.AssetId is not null)
            return Invalid("Provento não se repete.");

        if (!Enum.IsDefined(frequency))
            return Invalid("Frequência inválida.");

        if (endDate <= first.PurchaseDate)
            return Invalid($"O término deve ser depois do primeiro lançamento ({first.PurchaseDate:dd/MM/yyyy}).");

        if (autoDebit is not null && CheckAutoDebit(account, first.Type, frequency) is { } refused)
            return refused;

        var start = autoDebit?.DueDate ?? first.PurchaseDate;
        if (start > first.PurchaseDate || start < first.PurchaseDate.AddDays(-FirstDueDateMaxDaysBefore))
            return Invalid($"O vencimento fica até {FirstDueDateMaxDaysBefore} dias antes da data do lançamento.");

        var recurrence = new Recurrence
        {
            UserId = first.UserId,
            AccountId = account.Id,
            Type = first.Type,
            AmountCents = first.AmountCents,
            CategoryId = first.CategoryId,
            Description = first.Description,
            Method = autoDebit is null ? first.Method : PaymentMethod.Debit,
            Frequency = frequency,
            StartDate = start,
            EndDate = endDate,
            GeneratedThrough = start,
            Kind = autoDebit is null ? RecurrenceKind.Regular : RecurrenceKind.AutoDebit,
            AmountVaries = autoDebit?.AmountVaries ?? false,
        };
        first.LinkToRecurrence(recurrence.Id, start);
        return recurrence;
    }

    // Gera as ocorrências depois de GeneratedThrough e até hoje (regras 3 a 5, 8 e 9). category: a da
    // série, ou nenhuma se ela foi excluída. statements: as faturas do cartão, se for no cartão.
    public RecurrenceGeneration Generate(DateOnly today, Account account, Category? category, IReadOnlyCollection<Statement> statements)
    {
        if (account.Id != AccountId)
            throw new ArgumentException("A conta deve ser a da série.", nameof(account));

        if (category is not null && category.Id != CategoryId)
            throw new ArgumentException("A categoria deve ser a da série.", nameof(category));

        // No débito automático, só o que já foi debitado: o vencimento de domingo sai na segunda. O próximo dia
        // útil nunca volta no tempo, então o que fica de fora é sempre o fim da lista.
        var dates = RecurrenceSchedule.Between(StartDate, Frequency, EndDate, GeneratedThrough, today)
            .TakeWhile(date => TransactionDateOf(date) <= today)
            .ToList();
        if (dates.Count == 0)
            return RecurrenceGeneration.Nothing;

        // Conta inativa: as ocorrências do período não são lançadas nem ficam pendentes (regra 8).
        GeneratedThrough = dates[^1];
        if (!account.IsActive)
            return RecurrenceGeneration.Nothing;

        var known = statements.ToList();
        var created = new List<Transaction>();
        var opened = new List<Statement>();
        var pending = new List<PendingOccurrence>();

        foreach (var date in dates)
        {
            var transaction = OnCard ? Charge(account, category, date, known, opened, pending) : Simple(account, category, date);
            if (transaction is null)
                continue;

            transaction.LinkToRecurrence(Id, date);
            created.Add(transaction);
        }

        return new RecurrenceGeneration(created, opened, pending);
    }

    private Transaction? Charge(
        Account card, Category? category, DateOnly date, List<Statement> known, List<Statement> opened, List<PendingOccurrence> pending)
    {
        var purchase = CardPurchase.Create(UserId, card, Type, Method, AmountCents, 1, date, category, Description, known);
        if (purchase.IsSuccess)
        {
            known.AddRange(purchase.Value.OpenedStatements);
            opened.AddRange(purchase.Value.OpenedStatements);
            return purchase.Value.Installments[0];
        }

        // Fatura paga é intocável (regra 9): a cobrança espera a pessoa decidir.
        if (purchase.Error == CardPurchase.PurchaseIntoPaidStatement)
        {
            var statement = new CardPurchase.StatementPlacement(card, known, date).For(1);
            pending.Add(new PendingOccurrence(date, statement.Reference));
            return null;
        }

        throw new InvalidOperationException($"A série {Id} não gerou a cobrança de {date:yyyy-MM-dd}: {purchase.Error.Message}");
    }

    private Transaction Simple(Account account, Category? category, DateOnly date)
    {
        var transaction = Transaction.CreateSimple(UserId, account, Type, AmountCents, TransactionDateOf(date), category, Method, Description);
        if (!transaction.IsSuccess)
            throw new InvalidOperationException($"A série {Id} não gerou o lançamento de {date:yyyy-MM-dd}: {transaction.Error.Message}");

        // O valor que muda sai com a estimativa, a conferir (2.15, regra 5).
        if (Kind == RecurrenceKind.AutoDebit && AmountVaries)
            transaction.Value.MarkAmountEstimated();
        return transaction.Value;
    }

    // Resolve a cobrança pendente lançando-a (regra 9): na fatura seguinte à paga, presa; ou na própria, se o
    // pagamento foi desfeito. Sai com o cartão e o valor do dia em que venceu, e com a descrição e a categoria
    // atuais da série. card: o cartão da pendência; statements: as faturas dele.
    public Result<CardPurchaseResult> LaunchPending(
        RecurrencePending pending, Account card, Category? category, IReadOnlyCollection<Statement> statements, PendingLaunch where)
    {
        if (pending.RecurrenceId != Id)
            throw new ArgumentException("A pendência deve ser desta série.", nameof(pending));

        if (card.Id != pending.AccountId)
            throw new ArgumentException("O cartão deve ser o da pendência.", nameof(card));

        var launched = where == PendingLaunch.NextStatement
            ? CardPurchase.CreateInNextStatement(UserId, card, pending.AmountCents, pending.OccurrenceDate, category, Description, statements)
            : CardPurchase.Create(UserId, card, Type, Method, pending.AmountCents, 1, pending.OccurrenceDate, category, Description, statements);

        if (!launched.IsSuccess)
            return launched.Error == CardPurchase.PurchaseIntoPaidStatement
                ? Invalid("A fatura desta cobrança continua paga. Desfaça o pagamento ou lance na fatura seguinte.")
                : launched.Error;

        launched.Value.Installments[0].LinkToRecurrence(Id, pending.OccurrenceDate);
        return launched;
    }

    // Editar sem dizer o tipo mantém o tipo da série.
    public Result<Recurrence> Edit(
        Account account, long amountCents, Category? category, string? description, PaymentMethod method,
        RecurrenceFrequency frequency, DateOnly? nextDate, DateOnly? endDate) =>
        Edit(account, amountCents, category, description, method, frequency, nextDate, endDate, AutoDebit);

    // Editar vale do próximo em diante (regra 6): o que já foi gerado fica como está. Mudar a frequência
    // exige a próxima data, que vira a nova partida da agenda. autoDebit: o tipo daqui em diante (2.15, regra
    // 10); nulo, a série é comum. No débito automático o meio é sempre débito, e o informado não conta.
    public Result<Recurrence> Edit(
        Account account, long amountCents, Category? category, string? description, PaymentMethod method,
        RecurrenceFrequency frequency, DateOnly? nextDate, DateOnly? endDate, AutoDebitTerms? autoDebit)
    {
        var onCard = account.Type == AccountType.CreditCard;
        if (onCard != OnCard)
            return Invalid("A série continua no mesmo tipo de conta: cartão por cartão, conta por conta.");

        if (autoDebit is not null)
        {
            if (CheckAutoDebit(account, Type, frequency) is { } refused)
                return refused;
            method = PaymentMethod.Debit;
        }
        else if (onCard ? method != PaymentMethod.Credit : method == PaymentMethod.Credit)
            return Invalid(onCard ? "No cartão, a série usa o meio de pagamento crédito." : "Pagamento no crédito exige uma conta de cartão de crédito.");

        if (amountCents <= 0)
            return Invalid("O valor deve ser maior que zero.");

        if (category is not null && category.Type != Type)
            return Invalid("A categoria deve ser do mesmo tipo do lançamento (receita ou despesa).");

        var text = description?.Trim() ?? "";
        if (text.Length > Transaction.DescriptionMaxLength)
            return Invalid($"A descrição deve ter no máximo {Transaction.DescriptionMaxLength} caracteres.");

        if (!Enum.IsDefined(frequency))
            return Invalid("Frequência inválida.");

        if (frequency != Frequency && nextDate is null)
            return Invalid("Informe a próxima data da nova frequência.");

        if (nextDate <= GeneratedThrough)
            return Invalid($"A próxima data tem de ser depois do último lançamento ({GeneratedThrough:dd/MM/yyyy}).");

        if (endDate < GeneratedThrough)
            return Invalid($"O término não pode ser antes do último lançamento ({GeneratedThrough:dd/MM/yyyy}).");

        AccountId = account.Id;
        AmountCents = amountCents;
        CategoryId = category?.Id;
        Description = text;
        Method = method;
        Frequency = frequency;
        if (nextDate is { } next)
            StartDate = next;
        EndDate = endDate;
        Kind = autoDebit is null ? RecurrenceKind.Regular : RecurrenceKind.AutoDebit;
        AmountVaries = autoDebit?.AmountVaries ?? false;
        return this;
    }

    // Encerrar (regra 7): nada mais é gerado, nem a ocorrência de hoje que a tarefa ainda não gerou. O que
    // já foi gerado fica, porque é histórico.
    public void End() => EndDate = GeneratedThrough;

    // Débito automático: só despesa, só conta corrente, só mensal (2.15, regra 2).
    private static Error? CheckAutoDebit(Account account, TransactionType type, RecurrenceFrequency frequency)
    {
        if (account.Type != AccountType.Checking)
            return Invalid("Débito automático só sai de conta corrente.");

        if (type != TransactionType.Expense)
            return Invalid("Débito automático é sempre despesa.");

        if (frequency != RecurrenceFrequency.Monthly)
            return Invalid("Débito automático é todo mês.");

        return null;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
