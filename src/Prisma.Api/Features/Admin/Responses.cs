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

// A contagem de pessoas do admin (docs/admin-email.md, AE.2): só números, de uma janela de `Days` dias. Entre as contas com o
// e-mail confirmado, `Active + Inactive + NeverUsed = Confirmed`.
public sealed record UserSummaryResponse(
    int Days, int Accounts, int Confirmed, int Unconfirmed, int Active, int Inactive, int NeverUsed);

// Disparos de e-mail do admin (docs/admin-email.md, seção 4). Nada aqui descreve uma pessoa: só modelos, contagens e o histórico.
public sealed record EmailTemplateResponse(
    string Code, string Name, string Description, string Category, string Audience, int DefaultDays, int? MinDaysBetween,
    bool Sendable, string? BlockedCode);

public sealed record EmailAudienceResponse(string Template, int Days, int Count, int Cap);

public sealed record EmailPreviewResponse(string Subject, string Html, string Text);

public sealed record EmailCampaignResponse(Guid Id, string Template, int Days, int Recipients, DateTime CreatedAt);
