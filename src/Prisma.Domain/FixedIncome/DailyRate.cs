namespace Prisma.Domain.FixedIncome;

// A taxa diária de uma data, como a série publica: a SGS 12 do Banco Central traz o CDI em % ao dia com 6 casas
// (0,050788 %), que aqui é a fração 0,00050788 em unidades de 10⁻⁸ (50788). É o TDI da B3, já com as 8 casas dela.
// A taxa de uma data remunera de essa data até o dia útil seguinte.
public readonly record struct DailyRate(DateOnly Date, long RateE8);
