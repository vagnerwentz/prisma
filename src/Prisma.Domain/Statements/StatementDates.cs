namespace Prisma.Domain.Statements;

// Datas de um ciclo de fatura. Reference é o mês do vencimento ("2026-03").
public sealed record StatementDates(string Reference, DateOnly ClosingDate, DateOnly DueDate);
