using System.Numerics;
using CsCheck;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Domain.Tests.FixedIncome;

// Invariantes do motor para milhares de entradas (CLAUDE.md, seção 5). O oráculo é uma segunda implementação da regra
// da B3 em BigInteger (sem limite de tamanho): pega estouro e erro de representação do Int128. O algoritmo em si é
// provado pelos vetores independentes de FixedIncomeEngineTests.
public sealed class FixedIncomeEnginePropertyTests
{
    private static readonly DateOnly Start = new(2026, 1, 2);

    // Taxa diária de 0,00000001 a 0,0015 (até 0,15 % ao dia), de 1 a 300 dias seguidos.
    private static readonly Gen<List<long>> Rates = Gen.Long[1, 150_000].List[1, 300];

    private static RateHistory HistoryOf(List<long> rates) =>
        RateHistory.Create(rates.Select((r, i) => new DailyRate(Start.AddDays(i), r))).Value;

    private static (DateOnly Deposit, DateOnly AsOf) Dates(int count, int depositSeed, int spanSeed)
    {
        var deposit = Start.AddDays(depositSeed % count);
        var asOf = deposit.AddDays(spanSeed % (count + 3));
        return (deposit, asOf);
    }

    // Regra da B3 em BigInteger: fator diário 1 + p × TDI (exato, 14 casas), acumulado truncado a 16 casas, final com 8.
    private static (BigInteger FactorE8, BigInteger Gross) Oracle(long cents, long percent, IEnumerable<long> ratesE8)
    {
        BigInteger scale = BigInteger.Pow(10, 16);
        var accumulated = scale;
        foreach (var rate in ratesE8)
        {
            var daily = scale + new BigInteger(rate) * percent * 100;
            accumulated = accumulated * daily / scale;
        }

        var factorE8 = (accumulated + 50_000_000) / 100_000_000;
        var gross = (new BigInteger(cents) * factorE8 + 50_000_000) / 100_000_000;
        return (factorE8, gross);
    }

    [Fact]
    public void The_engine_matches_the_arbitrary_precision_oracle() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Int[100, 5_000_000], Gen.Long[1, 100_000_000_00])
            .Sample((rates, depositSeed, spanSeed, percent, cents) =>
            {
                var (deposit, asOf) = Dates(rates.Count, depositSeed, spanSeed);
                var result = FixedIncomeEngine.Balance(
                    new Money(cents), deposit, new IndexPercent(percent), HistoryOf(rates), asOf);

                var used = rates.Where((_, i) => Start.AddDays(i) >= deposit && Start.AddDays(i) < asOf);
                var (factor, gross) = Oracle(cents, percent, used);

                return result.IsSuccess
                    && result.Value.FactorE8 == (long)factor
                    && result.Value.GrossBalance.Cents == (long)gross;
            });

    [Fact]
    public void The_factor_never_goes_below_one_and_the_yield_is_never_negative() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Int[100, 5_000_000], Gen.Long[1, 100_000_000_00])
            .Sample((rates, depositSeed, spanSeed, percent, cents) =>
            {
                var (deposit, asOf) = Dates(rates.Count, depositSeed, spanSeed);
                var balance = FixedIncomeEngine.Balance(
                    new Money(cents), deposit, new IndexPercent(percent), HistoryOf(rates), asOf).Value;

                return balance.FactorE8 >= 100_000_000 && balance.GrossYield.Cents >= 0;
            });

    // Passar um dia nunca diminui o saldo.
    [Fact]
    public void The_balance_never_decreases_as_the_reference_date_moves_forward() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Int[100, 5_000_000], Gen.Long[1, 100_000_000_00])
            .Sample((rates, depositSeed, spanSeed, percent, cents) =>
            {
                var (deposit, asOf) = Dates(rates.Count, depositSeed, spanSeed);
                var history = HistoryOf(rates);
                var earlier = FixedIncomeEngine.Balance(new Money(cents), deposit, new IndexPercent(percent), history, asOf).Value;
                var later = FixedIncomeEngine.Balance(
                    new Money(cents), deposit, new IndexPercent(percent), history, asOf.AddDays(1)).Value;

                return later.GrossBalance.Cents >= earlier.GrossBalance.Cents;
            });

    // Mais percentual do CDI nunca rende menos.
    [Fact]
    public void A_higher_percent_never_yields_less() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Int[100, 2_500_000], Gen.Int[0, 2_500_000], Gen.Long[1, 100_000_000_00])
            .Sample((rates, depositSeed, spanSeed, percent, extra, cents) =>
            {
                var (deposit, asOf) = Dates(rates.Count, depositSeed, spanSeed);
                var history = HistoryOf(rates);
                var lower = FixedIncomeEngine.Balance(new Money(cents), deposit, new IndexPercent(percent), history, asOf).Value;
                var higher = FixedIncomeEngine.Balance(
                    new Money(cents), deposit, new IndexPercent(percent + extra), history, asOf).Value;

                return higher.GrossBalance.Cents >= lower.GrossBalance.Cents;
            });

    // O total do grupo é a soma dos saldos já arredondados, em qualquer ordem.
    [Fact]
    public void The_group_total_is_the_sum_of_the_lots_in_any_order() =>
        Gen.Select(Rates, Gen.Int[0, 1000], Gen.Int[0, 1000], Gen.Int[100, 5_000_000], Gen.Long[1, 100_000_000_00].List[1, 8])
            .Sample((rates, depositSeed, spanSeed, percent, principals) =>
            {
                var (deposit, asOf) = Dates(rates.Count, depositSeed, spanSeed);
                var history = HistoryOf(rates);
                var lots = principals
                    .Select(cents => FixedIncomeEngine.Balance(
                        new Money(cents), deposit, new IndexPercent(percent), history, asOf).Value)
                    .ToList();

                var totals = FixedIncomeTotals.Of(lots);
                var reversed = FixedIncomeTotals.Of(Enumerable.Reverse(lots));

                return totals.Principal.Cents == principals.Sum()
                    && totals.GrossBalance.Cents == lots.Sum(l => l.GrossBalance.Cents)
                    && totals.GrossYield.Cents == totals.GrossBalance.Cents - totals.Principal.Cents
                    && totals == reversed;
            });

    [Fact]
    public void A_history_with_a_repeated_date_is_always_refused() =>
        Gen.Select(Rates, Gen.Int[0, 1000]).Sample((rates, seed) =>
        {
            var index = seed % rates.Count;
            var entries = rates.Select((r, i) => new DailyRate(Start.AddDays(i), r)).ToList();
            entries.Insert(index, entries[index]);

            return !RateHistory.Create(entries).IsSuccess;
        });
}
