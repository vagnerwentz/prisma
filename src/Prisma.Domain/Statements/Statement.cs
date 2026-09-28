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

    public Result<Statement> EditDates(DateOnly closingDate, DateOnly dueDate) =>
        EditDates(closingDate, dueDate, previous: null, next: null);

    // Uma fatura não atravessa as vizinhas (docs/fase-2.md, 2.9, regra 5), na ordem de todo banco:
    // vencimento da anterior < fechamento < vencimento < fechamento da seguinte.
    public Result<Statement> EditDates(DateOnly closingDate, DateOnly dueDate, StatementDates? previous, StatementDates? next)
    {
        if (IsPaid)
            return new Error(ErrorType.Validation, "Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");

        if (dueDate < closingDate)
            return new Error(ErrorType.Validation, "O vencimento não pode ser antes do fechamento.");

        if (closingDate <= previous?.ClosingDate)
            return new Error(ErrorType.Validation,
                $"O fechamento tem de ser depois do fechamento da fatura anterior ({previous.ClosingDate:dd/MM}).");

        if (closingDate >= next?.ClosingDate)
            return new Error(ErrorType.Validation,
                $"O fechamento tem de ser antes do fechamento da fatura seguinte ({next.ClosingDate:dd/MM}).");

        if (closingDate <= previous?.DueDate)
            return new Error(ErrorType.Validation,
                $"O fechamento tem de ser depois do vencimento da fatura anterior ({previous.DueDate:dd/MM}).");

        if (dueDate >= next?.ClosingDate)
            return new Error(ErrorType.Validation,
                $"O vencimento tem de ser antes do fechamento da fatura seguinte ({next.ClosingDate:dd/MM}).");

        ClosingDate = closingDate;
        DueDate = dueDate;
        DatesEditedManually = true;
        return this;
    }
}
