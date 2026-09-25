using Prisma.Domain.Accounts;
using Prisma.Domain.Transactions;

namespace Prisma.Domain.Dashboard;

// Soma já agrupada das transações do mês, com o tipo da conta de cada uma.
public readonly record struct SummaryEntry(
    TransactionType Type, TransferDirection? Direction, AccountType AccountType, long AmountCents);

// Resumo de um mês pela SettlementDate (docs/fase-2.md, 2.1). Transferência nunca é receita nem
// despesa; só a que entra ou sai de conta de investimento conta, e como investido.
// CardExpenseCents: a parte das despesas feita no cartão (as faturas que vencem no mês).
public sealed record MonthlySummary(long IncomeCents, long ExpenseCents, long CardExpenseCents, long LeftoverCents, long InvestedCents)
{
    public static MonthlySummary Of(IEnumerable<SummaryEntry> entries)
    {
        long income = 0, expense = 0, cardExpense = 0, invested = 0;
        foreach (var entry in entries)
        {
            switch (entry.Type)
            {
                case TransactionType.Income:
                    income += entry.AmountCents;
                    break;
                case TransactionType.Expense:
                    expense += entry.AmountCents;
                    if (entry.AccountType == AccountType.CreditCard) cardExpense += entry.AmountCents;
                    break;
                case TransactionType.Transfer:
                    var sign = entry.Direction switch
                    {
                        TransferDirection.In => 1,
                        TransferDirection.Out => -1,
                        _ => throw new ArgumentException("Transferência sem direção.", nameof(entries)),
                    };
                    // Aporte entra na conta de investimento; resgate sai dela.
                    if (entry.AccountType == AccountType.Investment)
                        invested += sign * entry.AmountCents;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entries));
            }
        }

        // Sobra = receitas − despesas (decisão do usuário): investir é guardar a sobra, não gastar.
        return new MonthlySummary(income, expense, cardExpense, income - expense, invested);
    }
}
