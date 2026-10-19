namespace Prisma.Domain.FixedIncome;

// Quem disparou a sincronização: a tarefa no horário marcado, a tarefa ao subir (histórico vazio ou atrasado) ou uma pessoa
// pela tela de admin (docs/admin-mercado.md).
public enum MarketSyncKind { Scheduled, Startup, Manual }

public enum MarketSyncOutcome { Succeeded, SourceUnavailable, Failed, TimedOut }

// Uma execução da sincronização de uma série de mercado, como a tela de admin a mostra (docs/admin-mercado.md, 5). Dado
// operacional, o mesmo para todo mundo: não tem dono (CLAUDE.md, regra 6, exceção dos dados de mercado) e não guarda quem
// pediu (isso só vai ao log). Nunca guarda o corpo da resposta da fonte: o resumo do erro é um texto fixo por desfecho, e o
// código de erro do Banco Central (o número que a página de rejeição dele mostra) são só dígitos.
public sealed class MarketSyncRun
{
    public const int MaxSourceErrorCodeLength = 40;
    public const int MaxErrorSummaryLength = 200;

    // EF Core.
    private MarketSyncRun()
    {
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public ReferenceRateSeries Series { get; private init; }

    public MarketSyncKind Kind { get; private init; }

    public DateTime StartedAt { get; private init; }

    public DateTime FinishedAt { get; private init; }

    public MarketSyncOutcome Outcome { get; private init; }

    public DateOnly WindowFrom { get; private init; }

    public DateOnly WindowTo { get; private init; }

    public int Fetched { get; private init; }

    public int Added { get; private init; }

    public int Revised { get; private init; }

    public int Unchanged { get; private init; }

    public string? SourceErrorCode { get; private init; }

    public string? ErrorSummary { get; private init; }

    public static MarketSyncRun Succeeded(
        ReferenceRateSeries series, MarketSyncKind kind, DateTime startedAt, DateTime finishedAt, DateOnly windowFrom, DateOnly windowTo,
        int fetched, int added, int revised, int unchanged)
    {
        if (fetched < 0 || added < 0 || revised < 0 || unchanged < 0)
            throw new ArgumentOutOfRangeException(nameof(fetched), "As contagens não podem ser negativas.");

        return Create(series, kind, startedAt, finishedAt, windowFrom, windowTo, MarketSyncOutcome.Succeeded, null, fetched, added, revised, unchanged);
    }

    // `sourceErrorCode` só vale quando a fonte falhou, e só com dígitos (o número que o Banco Central pede num chamado).
    public static MarketSyncRun Unsuccessful(
        ReferenceRateSeries series, MarketSyncKind kind, DateTime startedAt, DateTime finishedAt, DateOnly windowFrom, DateOnly windowTo,
        MarketSyncOutcome outcome, string? sourceErrorCode = null)
    {
        if (outcome == MarketSyncOutcome.Succeeded)
            throw new ArgumentException("Uma execução que deu certo traz as contagens.", nameof(outcome));
        if (sourceErrorCode is not null && outcome != MarketSyncOutcome.SourceUnavailable)
            throw new ArgumentException("O código de erro da fonte só vale quando a fonte falhou.", nameof(sourceErrorCode));
        if (sourceErrorCode is not null
            && (sourceErrorCode.Length is 0 or > MaxSourceErrorCodeLength || !sourceErrorCode.All(char.IsAsciiDigit)))
            throw new ArgumentException("O código de erro da fonte são só dígitos, até 40.", nameof(sourceErrorCode));

        return Create(series, kind, startedAt, finishedAt, windowFrom, windowTo, outcome, sourceErrorCode, 0, 0, 0, 0);
    }

    // O resumo é um texto fixo por desfecho, em pt-BR: a tela o mostra como está, e nada do que a fonte respondeu entra nele.
    public static string? SummaryFor(MarketSyncOutcome outcome) => outcome switch
    {
        MarketSyncOutcome.Succeeded => null,
        MarketSyncOutcome.SourceUnavailable => "O Banco Central não respondeu como esperado.",
        MarketSyncOutcome.Failed => "A sincronização falhou por um erro interno.",
        MarketSyncOutcome.TimedOut => "A sincronização passou do tempo limite.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static MarketSyncRun Create(
        ReferenceRateSeries series, MarketSyncKind kind, DateTime startedAt, DateTime finishedAt, DateOnly windowFrom, DateOnly windowTo,
        MarketSyncOutcome outcome, string? sourceErrorCode, int fetched, int added, int revised, int unchanged)
    {
        if (finishedAt < startedAt)
            throw new ArgumentException("A execução não termina antes de começar.", nameof(finishedAt));
        if (windowFrom > windowTo)
            throw new ArgumentException("A janela começa depois de acabar.", nameof(windowFrom));

        return new MarketSyncRun
        {
            Series = series, Kind = kind, StartedAt = startedAt, FinishedAt = finishedAt, WindowFrom = windowFrom, WindowTo = windowTo,
            Outcome = outcome, SourceErrorCode = sourceErrorCode, ErrorSummary = SummaryFor(outcome),
            Fetched = fetched, Added = added, Revised = revised, Unchanged = unchanged,
        };
    }
}
