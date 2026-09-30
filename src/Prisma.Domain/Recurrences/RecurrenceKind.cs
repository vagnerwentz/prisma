namespace Prisma.Domain.Recurrences;

// O tipo da série (docs/fase-2.md, 2.15, D1). AutoDebit: o banco tira da conta corrente, no vencimento, o
// que a empresa cobra; o lançamento sai no próximo dia útil.
public enum RecurrenceKind { Regular, AutoDebit }

// Os termos de um débito automático (docs/fase-2.md, 2.15). AmountVaries: o valor da série é a estimativa, e
// cada ocorrência sai a conferir. DueDate: o vencimento da primeira ocorrência, só na criação (regra 4);
// sem ele, a data do lançamento.
public sealed record AutoDebitTerms(bool AmountVaries, DateOnly? DueDate = null);
