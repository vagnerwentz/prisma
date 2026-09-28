using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Statements;

// O que mudou ao editar as datas de uma fatura. MovedPurchases conta cada compra uma vez (a parcelada
// inteira é uma), para o aviso "N compras mudaram de fatura" (docs/fase-2.md, 2.9, tarefa 8).
public sealed record StatementDateEdit(Statement Statement, IReadOnlyList<Transaction> Moved, IReadOnlyList<Statement> Opened)
{
    public int MovedPurchases => Moved.Select(t => t.InstallmentPurchaseId ?? t.Id).Distinct().Count();
}

// Editar as datas de uma fatura, porque o banco antecipa ou adia fechamento e vencimento
// (docs/fase-1.md; docs/fase-2.md, 2.9, regras 4 e 5).
public static class StatementEditing
{
    // A fatura não atravessa as vizinhas (a que ainda não existe conta com as datas calculadas pelo
    // cartão, para a ordem nunca quebrar). Depois, o recálculo único reposiciona as compras do cartão:
    // a que mudou de ciclo anda inteira, e a que fica acompanha o novo vencimento.
    // statements: todas as faturas do cartão. transactions: as transações ativas do cartão.
    public static Result<StatementDateEdit> EditDates(
        Account card, Statement statement, IReadOnlyCollection<Statement> statements,
        IEnumerable<Transaction> transactions, DateOnly closingDate, DateOnly dueDate)
    {
        if (statement.AccountId != card.Id || !statements.Contains(statement))
            throw new ArgumentException("A fatura editada deve estar entre as faturas do cartão.", nameof(statement));

        var edited = statement.EditDates(closingDate, dueDate,
            Neighbor(card, statements, statement.Reference, -1), Neighbor(card, statements, statement.Reference, +1));
        if (!edited.IsSuccess)
            return edited.Error;

        var reconciliation = StatementReconciliation.Reconcile(card, statements, transactions);
        return new StatementDateEdit(statement, reconciliation.Moved, reconciliation.Opened);
    }

    private static StatementDates Neighbor(Account card, IReadOnlyCollection<Statement> statements, string reference, int months)
    {
        var neighbor = StatementCalculator.ShiftReference(reference, months);
        return statements.SingleOrDefault(s => s.Reference == neighbor)?.Dates
               ?? StatementCalculator.ForReference(neighbor, card.ClosingDay!.Value, card.DueDay!.Value);
    }
}
