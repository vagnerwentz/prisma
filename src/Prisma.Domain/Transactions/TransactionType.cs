namespace Prisma.Domain.Transactions;

// Refund (estorno) abate despesa e nunca é receita (docs/fase-2.md, 2.5).
// TradeSettlement (I.8, docs/investimentos.md): a liquidação de uma nota de corretagem vinculada a uma conta. Uma ponta
// só, com TransferDirection (Out: compra; In: venda). Mexe no saldo, mas não é receita, despesa nem aporte. Entra no fim
// do enum.
public enum TransactionType { Income, Expense, Transfer, Refund, TradeSettlement }
