namespace Prisma.Domain.Accounts;

public sealed class Account : Entity
{
    public const int NameMaxLength = 100;

    private Account() { }

    public string Name { get; private set; } = "";
    public AccountType Type { get; private set; }
    public long InitialBalanceCents { get; private set; }
    public int? ClosingDay { get; private set; }
    public int? DueDay { get; private set; }
    public long? CreditLimitCents { get; private set; }
    public bool IsActive { get; private set; }

    // Acompanhar o caixa (I.9, docs/patrimonio.md): só a conta de investimento, desligado por padrão. Ligado, o caixa livre da corretora
    // entra no patrimônio; desligado, o saldo da conta fica fora das somas do Resumo e só os ativos contam. O saldo e o extrato não mudam.
    public bool TracksCash { get; private set; }

    public const string TracksCashOnlyInvestment = "Só a conta de investimento acompanha o caixa.";

    public static Result<Account> Create(
        Guid userId, string name, AccountType type, long initialBalanceCents,
        int? closingDay, int? dueDay, long? creditLimitCents, bool tracksCash = false)
    {
        if (Validate(name, type, closingDay, dueDay, creditLimitCents) is { } error)
            return error;

        if (tracksCash && type != AccountType.Investment)
            return Invalid(TracksCashOnlyInvestment);

        return new Account
        {
            UserId = userId,
            Name = name.Trim(),
            Type = type,
            InitialBalanceCents = initialBalanceCents,
            ClosingDay = closingDay,
            DueDay = dueDay,
            CreditLimitCents = creditLimitCents,
            IsActive = true,
            TracksCash = tracksCash,
        };
    }

    // O tipo não é editável: trocar cartão por conta corrente invalidaria faturas e parcelas.
    public Result<Account> Update(
        string name, long initialBalanceCents, int? closingDay, int? dueDay,
        long? creditLimitCents, bool isActive, bool? tracksCash = null)
    {
        if (Validate(name, Type, closingDay, dueDay, creditLimitCents) is { } error)
            return error;

        // Sem o campo (a tela aberta antes do deploy não o manda), a escolha da pessoa fica como estava.
        if (tracksCash == true && Type != AccountType.Investment)
            return Invalid(TracksCashOnlyInvestment);

        Name = name.Trim();
        InitialBalanceCents = initialBalanceCents;
        ClosingDay = closingDay;
        DueDay = dueDay;
        CreditLimitCents = creditLimitCents;
        IsActive = isActive;
        TracksCash = tracksCash ?? TracksCash;
        return this;
    }

    // Para encerrar uma conta sem perder o histórico, o caminho é marcá-la como inativa.
    public Error? CheckCanDelete(int activeTransactionCount) =>
        activeTransactionCount > 0
            ? new Error(ErrorType.Conflict,
                "Esta conta tem transações. Mova ou exclua as transações antes, ou marque a conta como inativa.")
            : null;

    private static Error? Validate(
        string? name, AccountType type, int? closingDay, int? dueDay, long? creditLimitCents)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Invalid("Informe o nome da conta.");

        if (name.Trim().Length > NameMaxLength)
            return Invalid($"O nome da conta deve ter no máximo {NameMaxLength} caracteres.");

        if (type != AccountType.CreditCard)
        {
            if (closingDay is not null || dueDay is not null)
                return Invalid("Apenas cartão de crédito tem dia de fechamento e de vencimento.");

            if (creditLimitCents is not null)
                return Invalid("Apenas cartão de crédito tem limite.");

            return null;
        }

        if (closingDay is null)
            return Invalid("Cartão de crédito exige dia de fechamento.");

        if (dueDay is null)
            return Invalid("Cartão de crédito exige dia de vencimento.");

        if (closingDay is < 1 or > 31)
            return Invalid("O dia de fechamento deve estar entre 1 e 31.");

        if (dueDay is < 1 or > 31)
            return Invalid("O dia de vencimento deve estar entre 1 e 31.");

        if (creditLimitCents < 0)
            return Invalid("O limite do cartão não pode ser negativo.");

        return null;
    }

    private static Error Invalid(string message) => new(ErrorType.Validation, message);
}
