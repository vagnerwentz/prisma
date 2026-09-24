namespace Prisma.Domain.Transactions;

// Em transferência, Out é a ponta da conta de origem e In a da de destino (docs/fase-1.md, 2.3).
public enum TransferDirection { Out, In }
