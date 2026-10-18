using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Market.Bcb;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.FixedIncome.Coverage;

namespace Prisma.Api.Features.Rates;

// O que uma sincronização fez: quantas taxas vieram, entraram, foram revistas ou já estavam certas, e o estado do histórico
// depois (lacuna e defasagem).
public sealed record ReferenceRateSyncResult(int Fetched, int Added, int Revised, int Unchanged, ReferenceRateCoverage Coverage);

// Sincroniza o CDI diário do Banco Central com a tabela `reference_rates` (docs/renda-fixa.md, 3.2). Pede a janela
// [última data gravada − 10 dias, hoje] (a sobreposição pega correção recente; com a tabela vazia, desde o início do
// histórico configurado), deixa o domínio decidir o que entra, o que foi revisto e o que já estava certo
// (ReferenceRateReconciliation) e grava tudo numa transação só.
//
// Idempotente: rodar de novo, ou duas execuções ao mesmo tempo, dá o mesmo estado. A garantia final é o índice único
// (série, data): se outra execução gravou a mesma data entre a leitura e a gravação, a violação é esperada, e a tentativa
// seguinte relê, vê a linha nova e conclui que não há nada a fazer. Valor diferente para uma data gravada é revisão: grava,
// anota RevisedAt e loga em Warning, nunca em silêncio. Nunca apaga, nunca preenche lacuna com zero.
//
// Falha da fonte (MarketDataException) sobe para quem chamou com a tabela como estava; a próxima execução tenta de novo.
// A fonte e o banco saem de um escopo novo a cada execução (o HttpClient tipado não pode viver para sempre num singleton).
public sealed class ReferenceRateSync(
    IServiceScopeFactory scopes, IClock clock, IOptions<RateSyncOptions> options, ILogger<ReferenceRateSync> logger)
{
    private const int MaxAttempts = 3;

    // Chave do lock consultivo que serializa as sincronizações do CDI (qualquer número fixo, só precisa ser só deste uso).
    private const long SyncLockKey = 7_460_001;

    private const ReferenceRateSeries Series = ReferenceRateSeries.Cdi;

    // A janela normal: do último dia gravado (menos a sobreposição) até hoje.
    public async Task<ReferenceRateSyncResult> RunAsync(CancellationToken ct)
    {
        var settings = options.Value;
        DateOnly? last;
        await using (var scope = scopes.CreateAsyncScope())
            last = await LastStoredDate(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);

        var today = clock.Today;
        var from = last is { } date ? date.AddDays(-settings.OverlapDays) : settings.InitialLoadFrom;
        return await RefreshRangeAsync(from, today, ct);
    }

    // Reprocessa um período à escolha (corrigir lacuna, conferir uma revisão). É a mesma sincronização com outra janela, e é
    // idempotente do mesmo jeito. Hoje só se chama de código/teste: não há endpoint.
    public async Task<ReferenceRateSyncResult> RefreshRangeAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IReferenceRateSource>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        IReadOnlyList<DailyRate>? incoming = null;
        ReferenceRateChanges changes;
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                // Uma sincronização por vez no banco inteiro (inclusive com duas instâncias da API): o lock é do Postgres e
                // cai sozinho no fim da transação. Sem ele, várias execuções gravando as mesmas datas se bloqueiam em ordens
                // diferentes e perdem por impasse ou por índice único, e repetir não converge.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SyncLockKey})", ct);

                var existing = await db.ReferenceRates
                    .Where(r => r.Series == Series && r.ReferenceDate >= from && r.ReferenceDate <= to)
                    .ToListAsync(ct);

                // Uma busca só: as tentativas seguintes reaproveitam o que veio. Dentro do lock, para quem esperou a vez já
                // encontrar o trabalho feito.
                incoming ??= await source.FetchAsync(from, to, ct);

                changes = ReferenceRateReconciliation.Apply(Series, existing, incoming, BcbSgsRateSource.Provider, clock.UtcNow);
                db.ReferenceRates.AddRange(changes.Added);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                break;
            }
            // Alguém de fora do lock gravou as mesmas datas no meio do caminho (ou o Postgres abortou esta por impasse): a
            // transação foi desfeita, e a tentativa seguinte relê e confere de novo.
            catch (Exception ex) when (attempt < MaxAttempts && IsLostRace(ex))
            {
            }
        }

        logger.ReferenceRatesSynced(BcbSgsRateSource.Provider, incoming.Count, changes.Added.Count, changes.Revised.Count, changes.Unchanged);
        foreach (var date in changes.Revised)
            logger.ReferenceRateRevised(BcbSgsRateSource.Provider, date);

        var coverage = await Diagnose(db, ct);
        return new ReferenceRateSyncResult(incoming.Count, changes.Added.Count, changes.Revised.Count, changes.Unchanged, coverage);
    }

    // A primeira subida (nada gravado) ou o histórico atrasado (faltou a sincronização de algum dia): roda logo, sem esperar o
    // horário marcado.
    public async Task<bool> NeedsRunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var last = await LastStoredDate(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
        return last is null || last < ReferenceRateCoverage.ExpectedLastDate(clock.Today);
    }

    private async Task<ReferenceRateCoverage> Diagnose(AppDbContext db, CancellationToken ct)
    {
        var dates = await db.ReferenceRates.AsNoTracking().Where(r => r.Series == Series).Select(r => r.ReferenceDate).ToListAsync(ct);
        var coverage = ReferenceRateCoverage.Analyze(dates, clock.Today);
        if (!coverage.IsComplete)
            logger.ReferenceRateGapDetected(
                BcbSgsRateSource.Provider, coverage.MissingBusinessDays.Count, coverage.MissingBusinessDays[0], coverage.MissingBusinessDays[^1]);
        if (coverage.IsStale)
            logger.ReferenceRatesStale(BcbSgsRateSource.Provider, coverage.Last, coverage.ExpectedLast);
        return coverage;
    }

    // O EF trata o impasse como falha transitória e o entrega embrulhado num InvalidOperationException (como em
    // ConcurrentStatementOpening).
    private static bool IsLostRace(Exception ex) =>
        (ex as DbUpdateException ?? ex.InnerException as DbUpdateException) is { } update
        && (update.IsUniqueViolation(ReferenceRateConfiguration.SeriesDateIndex) || update.IsDeadlock());

    private static Task<DateOnly?> LastStoredDate(AppDbContext db, CancellationToken ct) =>
        db.ReferenceRates.Where(r => r.Series == Series).MaxAsync(r => (DateOnly?)r.ReferenceDate, ct);
}
