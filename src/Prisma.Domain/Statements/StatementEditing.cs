using Prisma.Domain.Transactions;

namespace Prisma.Domain.Statements;

// Editar as datas de uma fatura recalcula o SettlementDate das transações dela (docs/fase-1.md),
// porque o banco antecipa ou adia fechamento e vencimento em fim de semana e feriado. As
// transações continuam na mesma fatura: só a data de caixa acompanha o novo vencimento.
public static class StatementEditing
{
    public static Result<Statement> EditDates(
        Statement statement, IReadOnlyList<Transaction> transactions, DateOnly closingDate, DateOnly dueDate)
    {
        if (transactions.Any(t => t.StatementId != statement.Id))
            throw new ArgumentException("Todas as transações devem pertencer à fatura editada.", nameof(transactions));

        var edited = statement.EditDates(closingDate, dueDate);
        if (!edited.IsSuccess)
            return edited.Error;

        foreach (var transaction in transactions)
            transaction.SettleOn(statement);

        return statement;
    }
}
