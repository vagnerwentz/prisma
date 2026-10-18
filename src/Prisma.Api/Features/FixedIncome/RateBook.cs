using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.FixedIncome.Coverage;

namespace Prisma.Api.Features.FixedIncome;

// O estado do histórico do CDI que acompanha todo número calculado (docs/renda-fixa.md, 3.3 e 6.3): se há taxa, até que dia vai,
// se está atrasado e se falta algum dia útil no meio. Quem mostra o saldo mostra isto junto ("rendimento até 15/10"): o saldo
// nunca finge estar em dia.
public sealed record RatesStatus(bool Available, DateOnly? Through, bool Stale, bool Complete, DateOnly ExpectedLast);

// As taxas gravadas, prontas para o motor, e o estado delas. Lê o histórico inteiro de uma vez (cerca de 250 linhas por ano): é
// pouco, e assim o saldo de qualquer aporte e a lacuna saem da mesma leitura, sem consultar o banco por aporte.
public sealed class RateBook(RateHistory history, RatesStatus status)
{
    public RateHistory History { get; } = history;

    public RatesStatus Status { get; } = status;

    public static async Task<RateBook> Load(AppDbContext db, IClock clock, CancellationToken ct)
    {
        var rows = await db.ReferenceRates.AsNoTracking()
            .Where(r => r.Series == ReferenceRateSeries.Cdi)
            .OrderBy(r => r.ReferenceDate)
            .Select(r => new DailyRate(r.ReferenceDate, r.DailyRateE8))
            .ToListAsync(ct);

        // Já vêm em ordem, sem repetir e dentro dos limites: o banco garante (índice único e constraints).
        var history = RateHistory.Create(rows).Value;
        var coverage = ReferenceRateCoverage.Analyze(rows.Select(r => r.Date), clock.Today);
        return new RateBook(history, new RatesStatus(
            Available: coverage.Last is not null, Through: coverage.Last, Stale: coverage.IsStale,
            Complete: coverage.IsComplete, ExpectedLast: coverage.ExpectedLast));
    }
}
