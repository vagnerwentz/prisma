namespace Prisma.Domain.Recurrences;

// Onde lançar a cobrança pendente (docs/fase-2.md, 2.14, regra 9): na fatura seguinte à paga, presa a ela;
// ou na própria fatura, depois de a pessoa desfazer o pagamento. A terceira saída, descartar, não lança.
public enum PendingLaunch { NextStatement, SameStatement }
