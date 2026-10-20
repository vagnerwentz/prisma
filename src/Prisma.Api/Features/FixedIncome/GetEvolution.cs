using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.FixedIncome;

public sealed record EvolutionPoint(DateOnly Date, long PrincipalCents, long GrossBalanceCents, long GrossYieldCents);

public sealed record EvolutionResponse(
    string Scope, DateOnly From, DateOnly To, RatesStatus Rates, int UnavailableDepositCount, IReadOnlyList<EvolutionPoint> Points);

// A evolução do saldo bruto ao longo do tempo, para o gráfico (docs/renda-fixa.md, 6.2): calculada sob demanda, sem guardar um
// registro por dia. Para cada aporte, uma passada só pelas taxas (FixedIncomeEngine.Series); os saldos dos aportes da caixa somam por data.
//
// Pontos: cada dia com taxa publicada, cada dia de aporte ou de resgate e o fim do período. O ponto de uma data é o saldo da balances com
// asOf igual a ela (mesmo motor, mesma regra). Aporte sem taxa para a data dele entra pelo principal e é contado, como na balances.
// `step=week|month` devolve só o último ponto de cada semana (começa na segunda) ou mês, mais o primeiro e o último do período.
public static class GetEvolution
{
    public const string All = "all";
    public const string None = "none";

    public sealed class Handler(AppDbContext db, IClock clock)
    {
        public async Task<Result<EvolutionResponse>> Execute(string? goal, DateOnly? fromRequest, DateOnly? toRequest, string? step, CancellationToken ct)
        {
            var today = clock.Today;
            var to = toRequest ?? today;
            if (to > today)
                return new Error(ErrorType.Validation, "A data final não pode estar no futuro: a evolução mostra o que já rendeu.");
            if (step is not (null or "day" or "week" or "month"))
                return new Error(ErrorType.Validation, "O passo do gráfico é day, week ou month.");

            var scope = string.IsNullOrEmpty(goal) ? All : goal;
            var query = db.FixedIncomeDeposits.AsNoTracking().Where(d => d.DepositDate <= to);
            if (scope == None)
            {
                query = query.Where(d => d.GoalId == null);
            }
            else if (scope != All)
            {
                if (!Guid.TryParse(scope, out var goalId))
                    return new Error(ErrorType.Validation, "Objetivo inválido.");
                if (!await Goals.Exists(db, goalId, ct))
                    return new Error(ErrorType.NotFound, "Objetivo não encontrado.");
                query = query.Where(d => d.GoalId == goalId);
            }

            var deposits = await query.ToListAsync(ct);
            var book = await RateBook.Load(db, clock, ct);
            var from = fromRequest ?? (deposits.Count > 0 ? deposits.Min(d => d.DepositDate) : to);
            if (from > to)
                return new Error(ErrorType.Validation, "A data inicial não pode ser depois da final.");

            var depositIds = deposits.Select(d => d.Id).ToList();
            var redemptions = (await db.FixedIncomeRedemptions.AsNoTracking()
                    .Where(r => depositIds.Contains(r.DepositId) && r.RedemptionDate <= to).ToListAsync(ct))
                .ToLookup(r => r.DepositId);
            var dates = SampleDates(book, deposits, from, to, step ?? "day", redemptions.SelectMany(g => g).Select(r => r.RedemptionDate));
            var principal = new long[dates.Count];
            var gross = new long[dates.Count];
            var unavailable = 0;
            foreach (var deposit in deposits)
            {
                var steps = redemptions[deposit.Id]
                    .Select(r => new RedemptionStep(r.RedemptionDate, new Money(r.PrincipalCents))).ToList();
                var series = FixedIncomeEngine.Series(
                    new Money(deposit.AmountCents), deposit.DepositDate, deposit.Remuneration, book.History, dates, steps);
                if (!series.IsSuccess)
                    unavailable++;

                for (var i = 0; i < dates.Count; i++)
                {
                    if (dates[i] < deposit.DepositDate)
                        continue;

                    // O que sobra do aporte na data: os resgates até ela já saíram.
                    var remaining = deposit.AmountCents - steps.Where(s => s.Date <= dates[i]).Sum(s => s.Principal.Cents);
                    principal[i] += remaining;
                    gross[i] += series.IsSuccess ? series.Value[i]!.Value.Cents : remaining;
                }
            }

            var points = dates.Select((date, i) => new EvolutionPoint(date, principal[i], gross[i], gross[i] - principal[i])).ToList();
            return new EvolutionResponse(scope, from, to, book.Status, unavailable, points);
        }

        // Os dias que valem um ponto: cada dia com taxa, cada dia de aporte e as pontas do período. Em ordem crescente, sem repetir.
        private static List<DateOnly> SampleDates(
            RateBook book, List<FixedIncomeDeposit> deposits, DateOnly from, DateOnly to, string step, IEnumerable<DateOnly> redemptionDates)
        {
            var all = new SortedSet<DateOnly> { from, to };
            for (var i = 0; i < book.History.Count; i++)
                if (book.History[i].Date >= from && book.History[i].Date <= to)
                    all.Add(book.History[i].Date);
            foreach (var deposit in deposits)
                if (deposit.DepositDate >= from && deposit.DepositDate <= to)
                    all.Add(deposit.DepositDate);

            // O dia de um resgate também é um ponto: é onde o saldo cai.
            foreach (var date in redemptionDates)
                if (date >= from && date <= to)
                    all.Add(date);

            var dates = all.ToList();
            if (step == "day")
                return dates;

            // Um ponto por semana ou mês: o último de cada um, mais o primeiro e o último do período.
            // Semana começa na segunda; o balde é o dia em que ela começou (ou ano × 12 + mês).
            Func<DateOnly, int> bucket = step == "week"
                ? d => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)).DayNumber
                : d => d.Year * 12 + d.Month;
            var kept = new List<DateOnly> { dates[0] };
            for (var i = 1; i < dates.Count; i++)
                if (i == dates.Count - 1 || bucket(dates[i]) != bucket(dates[i + 1]))
                    kept.Add(dates[i]);
            return kept.Distinct().ToList();
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/evolution", async (string? goal, DateOnly? from, DateOnly? to, string? step, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(goal, from, to, step, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<EvolutionResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
}
