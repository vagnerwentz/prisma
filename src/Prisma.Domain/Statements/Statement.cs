namespace Prisma.Domain.Statements;

// Fatura do cartão. Nasce com as datas do StatementCalculator; o usuário pode editá-las,
// porque o banco antecipa ou adia o fechamento em fim de semana e feriado.
public sealed class Statement : Entity
{
    public const int ReferenceLength = 7;

    private Statement() { }

    public Guid AccountId { get; private set; }
    public string Reference { get; private set; } = "";
    public DateOnly ClosingDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public bool IsPaid { get; private set; }
    public bool DatesEditedManually { get; private set; }

    public StatementDates Dates => new(Reference, ClosingDate, DueDate);

    public static Statement Open(Guid userId, Guid accountId, StatementDates dates) =>
        new()
        {
            UserId = userId,
            AccountId = accountId,
            Reference = dates.Reference,
            ClosingDate = dates.ClosingDate,
            DueDate = dates.DueDate,
        };

    // Usado pelo pagamento de fatura (etapa 1.10). Parcelas em fatura paga não são redistribuídas.
    public void MarkAsPaid() => IsPaid = true;

    public Result<Statement> EditDates(DateOnly closingDate, DateOnly dueDate)
    {
        if (dueDate < closingDate)
            return new Error(ErrorType.Validation, "O vencimento não pode ser antes do fechamento.");

        ClosingDate = closingDate;
        DueDate = dueDate;
        DatesEditedManually = true;
        return this;
    }
}
