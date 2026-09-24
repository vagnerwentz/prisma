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

    // Pagamento de fatura (etapa 1.10). Fatura paga não muda de valor (docs/fase-1.md, 2.3).
    public void MarkAsPaid() => IsPaid = true;

    // Excluir a transferência de pagamento desfaz o pagamento.
    internal void MarkAsUnpaid() => IsPaid = false;

    public Result<Statement> EditDates(DateOnly closingDate, DateOnly dueDate)
    {
        if (IsPaid)
            return new Error(ErrorType.Validation, "Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");

        if (dueDate < closingDate)
            return new Error(ErrorType.Validation, "O vencimento não pode ser antes do fechamento.");

        ClosingDate = closingDate;
        DueDate = dueDate;
        DatesEditedManually = true;
        return this;
    }
}
