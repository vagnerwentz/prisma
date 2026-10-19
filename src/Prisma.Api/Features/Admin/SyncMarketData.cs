using Microsoft.Extensions.Options;
using Prisma.Api.Features.Rates;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Features.Admin;

// Dispara a sincronização à mão (docs/admin-mercado.md, 4 e 8): a janela normal (`sync`) ou um período à escolha (`refresh`). É a
// mesma `ReferenceRateSync` da tarefa, com o mesmo lock: duas execuções ao mesmo tempo não duplicam, e a segunda espera a vez, até o
// teto. Não mexe nos horários da tarefa. A execução fica gravada, e quem pediu só vai ao log.
public static class SyncMarketData
{
    public static readonly Error UnknownSeries = new(ErrorType.NotFound, "Série desconhecida.", "UnknownSeries");

    public sealed record RefreshRequest(DateOnly From, DateOnly To);

    public sealed class Handler(
        ReferenceRateSync sync, IClock clock, IOptions<RateSyncOptions> rates, IOptions<AdminOptions> admin, ICurrentUser currentUser,
        ILogger<Handler> logger)
    {
        public Task<Result<MarketSyncRun>> Sync(string seriesCode, CancellationToken ct) => Run(seriesCode, null, null, ct);

        public async Task<Result<MarketSyncRun>> Refresh(string seriesCode, RefreshRequest request, CancellationToken ct)
        {
            if (MarketSeriesCatalog.Find(seriesCode) is null)
                return UnknownSeries;

            var problem = SyncWindow.Validate(request.From, request.To, clock.Today, rates.Value.InitialLoadFrom);
            if (problem is not null)
                return new Error(ErrorType.Validation, problem, "InvalidWindow");

            return await Run(seriesCode, request.From, request.To, ct);
        }

        private async Task<Result<MarketSyncRun>> Run(string seriesCode, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (MarketSeriesCatalog.Find(seriesCode) is not { } series)
                return UnknownSeries;

            logger.MarketDataSyncRequested(currentUser.UserId, series.Code, from, to);
            return await sync.RunRecordedAsync(
                MarketSyncKind.Manual, from, to, TimeSpan.FromSeconds(admin.Value.SyncTimeoutSeconds), ct);
        }
    }

    // A execução dá certo (200) ou a fonte, o tempo ou o servidor falharam: a tela reconhece pelo `code`, sem depender do texto.
    public static IResult ToResult(MarketSyncRun run)
    {
        var body = MarketSyncRunResponse.Of(run);
        return run.Outcome switch
        {
            MarketSyncOutcome.Succeeded => Results.Ok(body),
            MarketSyncOutcome.SourceUnavailable => Problem(StatusCodes.Status502BadGateway, "SourceUnavailable", run, body),
            MarketSyncOutcome.TimedOut => Problem(StatusCodes.Status504GatewayTimeout, "SyncTimedOut", run, body),
            _ => Problem(StatusCodes.Status500InternalServerError, "SyncFailed", run, body),
        };
    }

    private static IResult Problem(int status, string code, MarketSyncRun run, MarketSyncRunResponse body) =>
        Results.Problem(
            detail: run.ErrorSummary, statusCode: status, title: "A sincronização não terminou.",
            extensions: new Dictionary<string, object?> { ["code"] = code, ["sourceErrorCode"] = run.SourceErrorCode, ["run"] = body });

    public static void MapSync(IEndpointRouteBuilder app) =>
        app.MapPost("/{series}/sync", async (string series, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Sync(series, ct);
                return result.IsSuccess ? ToResult(result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.AdminSyncRateLimitPolicy)
            .Produces<MarketSyncRunResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(502)
            .ProducesProblem(504);

    public static void MapRefresh(IEndpointRouteBuilder app) =>
        app.MapPost("/{series}/refresh", async (string series, RefreshRequest request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Refresh(series, request, ct);
                return result.IsSuccess ? ToResult(result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.AdminSyncRateLimitPolicy)
            .Produces<MarketSyncRunResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(502)
            .ProducesProblem(504);
}
