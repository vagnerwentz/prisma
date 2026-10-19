using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.Admin;

// Uma execução da sincronização, como a tela a mostra (docs/admin-mercado.md, 4). A série vai pelo código da rota ("cdi").
public sealed record MarketSyncRunResponse(
    Guid Id, string Series, MarketSyncKind Kind, DateTime StartedAt, DateTime FinishedAt, MarketSyncOutcome Outcome,
    DateOnly WindowFrom, DateOnly WindowTo, int Fetched, int Added, int Revised, int Unchanged, string? SourceErrorCode, string? ErrorSummary)
{
    public static MarketSyncRunResponse Of(MarketSyncRun run) => new(
        run.Id, MarketSeriesCatalog.CodeOf(run.Series), run.Kind, run.StartedAt, run.FinishedAt, run.Outcome, run.WindowFrom, run.WindowTo,
        run.Fetched, run.Added, run.Revised, run.Unchanged, run.SourceErrorCode, run.ErrorSummary);
}

// Quando a tarefa roda sozinha: os horários de São Paulo e o próximo instante. `Enabled` falso = a tarefa está desligada.
public sealed record MarketSyncSchedule(IReadOnlyList<string> RunTimes, DateTime NextRunUtc, bool Enabled);

public sealed record MarketSeriesStatus(
    string Code, string Name, string Source, int Count, DateOnly? First, DateOnly? Last, DateOnly ExpectedLast, bool Stale, bool Complete,
    int MissingBusinessDayCount, DateTime? LastFetchedAt, DateTime? LastRevisedAt, MarketSyncSchedule Schedule,
    MarketSyncRunResponse? LastRun, MarketSyncRunResponse? LastSuccessfulRun);
