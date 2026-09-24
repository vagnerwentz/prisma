namespace Prisma.Domain.Statements;

// Regras de docs/fase-1.md, seção 2.1. Um ciclo é identificado pelo mês em que fecha;
// a Reference exposta é o mês do vencimento.
public static class StatementCalculator
{
    public static StatementDates ForPurchase(
        DateOnly purchaseDate, int closingDay, int dueDay, IReadOnlyCollection<StatementDates> existing) =>
        ForInstallment(purchaseDate, 1, closingDay, dueDay, existing);

    // A parcela i entra i-1 ciclos após o statement da compra (regra 2.2).
    // "existing" são as faturas já gravadas do cartão: as datas delas prevalecem (regra 5).
    public static StatementDates ForInstallment(
        DateOnly purchaseDate, int installmentNumber, int closingDay, int dueDay,
        IReadOnlyCollection<StatementDates> existing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(installmentNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(closingDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(closingDay, 31);
        ArgumentOutOfRangeException.ThrowIfLessThan(dueDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dueDay, 31);

        var byReference = existing.ToDictionary(s => s.Reference);

        StatementDates Effective(int cycle)
        {
            var calculated = Calculate(cycle, closingDay, dueDay);
            return byReference.GetValueOrDefault(calculated.Reference) ?? calculated;
        }

        // Começa um ciclo antes do mês da compra: um fechamento adiado do ciclo anterior
        // pode ainda alcançar a compra. Regra 1: primeiro ciclo que fecha na data ou depois.
        var purchaseCycle = CycleOf(purchaseDate.Year, purchaseDate.Month) - 1;
        while (Effective(purchaseCycle).ClosingDate < purchaseDate)
            purchaseCycle++;

        return Effective(purchaseCycle + installmentNumber - 1);
    }

    private static StatementDates Calculate(int cycle, int closingDay, int dueDay)
    {
        var closingDate = DayInMonth(cycle, closingDay);

        // Regra 3: vencimento igual ou anterior ao fechamento cai no mês seguinte.
        var dueCycle = dueDay > closingDay ? cycle : cycle + 1;
        var dueDate = DayInMonth(dueCycle, dueDay);

        return new StatementDates($"{dueDate.Year:D4}-{dueDate.Month:D2}", closingDate, dueDate);
    }

    private static int CycleOf(int year, int month) => year * 12 + (month - 1);

    // Regra 2: dia além do fim do mês vira o último dia do mês.
    private static DateOnly DayInMonth(int cycle, int day)
    {
        var (year, month) = (cycle / 12, cycle % 12 + 1);
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }
}
