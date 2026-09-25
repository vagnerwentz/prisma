namespace Prisma.Domain.Transactions;

// Refund (estorno) abate despesa e nunca é receita (docs/fase-2.md, 2.5).
public enum TransactionType { Income, Expense, Transfer, Refund }
