namespace Prisma.Domain.FixedIncome;

// O saldo bruto estimado de um aporte numa data. Nada daqui é gravado: sai do aporte e das taxas (docs/renda-fixa.md, 2.3).
//
// FactorE8: o fator acumulado da B3, com 8 casas (1,03193390 = 103193390). RatesUsed: quantas taxas diárias entraram.
// RatesThrough: a data da última taxa usada (nulo se nenhuma entrou, e sempre nulo no pré-fixado, que não usa taxas do CDI). Se for anterior ao dia útil esperado, as taxas
// estão defasadas, e quem mostra o saldo diz "rendimento até <data>".
public sealed record FixedIncomeBalance(Money Principal, Money GrossBalance, long FactorE8, int RatesUsed, DateOnly? RatesThrough)
{
    public Money GrossYield => new(GrossBalance.Cents - Principal.Cents);
}

// O grupo (objetivo) é a soma dos saldos dos aportes, cada um já arredondado em centavos: o total sempre bate com a
// soma que a pessoa vê (docs/renda-fixa.md, 5.1, regra 5). Nunca se arredonda o total.
public sealed record FixedIncomeTotals(Money Principal, Money GrossBalance, DateOnly? RatesThrough)
{
    public Money GrossYield => new(GrossBalance.Cents - Principal.Cents);

    // RatesThrough do grupo é a taxa mais antiga entre as últimas de cada aporte: o grupo só vale até onde todos valem.
    public static FixedIncomeTotals Of(IEnumerable<FixedIncomeBalance> balances)
    {
        long principal = 0, gross = 0;
        DateOnly? through = null;
        foreach (var balance in balances)
        {
            principal = checked(principal + balance.Principal.Cents);
            gross = checked(gross + balance.GrossBalance.Cents);
            if (balance.RatesThrough is { } date && (through is null || date < through))
                through = date;
        }

        return new FixedIncomeTotals(new Money(principal), new Money(gross), through);
    }
}
