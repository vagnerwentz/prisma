using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Market.Brapi;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// Cliente da lista de ativos da brapi (docs/investimentos.md, seção 8, etapa 1). As fixtures são respostas
// reais da brapi (2026-10-03), cortadas em duas páginas com um ativo de cada tipo.
public sealed class BrapiAssetListSourceTests
{
    private readonly LogSink _logs = new();

    [Fact]
    public async Task Joins_all_pages_into_one_list()
    {
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Fixture($"brapi-tickers-page-{StubHttpHandler.Query(request, "page")}.json"));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        list.Pages.ShouldBe(2);
        list.Skipped.ShouldBe(0);
        list.Assets.Select(a => (a.Symbol, a.Kind)).ShouldBe(
        [
            ("BBAS3", AssetKind.Stock),
            ("ITSA4", AssetKind.Stock),
            ("MXRF11", AssetKind.Fii),
            ("TAEE11", AssetKind.Unit),
            ("AAPL34", AssetKind.Bdr),
            ("AAGR11", AssetKind.FiAgro),
            ("AZIN11", AssetKind.FiInfra),
            ("BDIV11", AssetKind.Fip),
            ("BDOM", AssetKind.OtherFund),
            ("BOVA11", AssetKind.Etf),
            ("CAUT11", AssetKind.Fidc),
        ]);
    }

    [Fact]
    public async Task Keeps_both_names_because_a_fund_name_is_its_symbol()
    {
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Fixture($"brapi-tickers-page-{StubHttpHandler.Query(request, "page")}.json"));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        var fii = list.Assets.Single(a => a.Symbol == "MXRF11");
        fii.Name.ShouldBe("MXRF11");
        fii.LongName.ShouldBe("Maxi Renda Fundo de Investimento Imobiliario Cotas");
        list.Assets.Single(a => a.Symbol == "BBAS3").Name.ShouldBe("BCO BRASIL S.A.");
    }

    [Fact]
    public async Task Keeps_real_logos_and_drops_the_brapi_placeholder()
    {
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Fixture($"brapi-tickers-page-{StubHttpHandler.Query(request, "page")}.json"));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        list.Assets.Single(a => a.Symbol == "BBAS3").LogoUrl.ShouldBe("https://icons.brapi.dev/icons/BBAS3.svg");
        // Fundo sem logo próprio: a brapi aponta para o BRAPI.svg, que para nós é "sem logo".
        list.Assets.Single(a => a.Symbol == "MXRF11").LogoUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Asks_for_pages_sorted_by_symbol_with_the_configured_size()
    {
        // A ordem padrão é por volume, que muda entre uma página e outra: um ativo sairia duas vezes ou nenhuma.
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Fixture($"brapi-tickers-page-{StubHttpHandler.Query(request, "page")}.json"));

        await Source(handler, new BrapiOptions { PageSize = 500 }).FetchAllAsync(CancellationToken.None);

        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ShouldAllBe(path => path == "/api/v2/tickers");
        handler.Requests.Select(r => StubHttpHandler.Query(r, "page")).ShouldBe(["1", "2"]);
        handler.Requests.ShouldAllBe(r =>
            StubHttpHandler.Query(r, "limit") == "500" &&
            StubHttpHandler.Query(r, "sortBy") == "symbol" &&
            StubHttpHandler.Query(r, "sortOrder") == "asc");
    }

    [Theory]
    [InlineData("stock", "stock", AssetKind.Stock)]
    [InlineData("stock", null, AssetKind.Stock)]
    [InlineData("stock", "unit", AssetKind.Unit)]
    [InlineData("STOCK", " Unit ", AssetKind.Unit)]
    [InlineData("fund", "fii", AssetKind.Fii)]
    [InlineData("fund", "etf", AssetKind.Etf)]
    [InlineData("fund", "fi-infra", AssetKind.FiInfra)]
    [InlineData("fund", "fi-agro", AssetKind.FiAgro)]
    [InlineData("fund", "fip", AssetKind.Fip)]
    [InlineData("fund", "fidc", AssetKind.Fidc)]
    [InlineData("fund", null, AssetKind.OtherFund)]
    [InlineData("fund", "", AssetKind.OtherFund)]
    [InlineData("bdr", "bdr", AssetKind.Bdr)]
    [InlineData("bdr", null, AssetKind.Bdr)]
    [InlineData("fund", "fiagro-novo", AssetKind.Unknown)]
    [InlineData("stock", "fii", AssetKind.Unknown)]
    [InlineData("crypto", null, AssetKind.Unknown)]
    [InlineData(null, null, AssetKind.Unknown)]
    public async Task Translates_the_brapi_type_into_our_kind(string? assetType, string? subType, AssetKind expected)
    {
        var handler = new StubHttpHandler(_ => Page(1, hasNextPage: false, Item("ABCD3", assetType, subType)));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        list.Assets.Single().Kind.ShouldBe(expected);
    }

    [Fact]
    public async Task Unknown_kind_enters_the_list_and_warns_once_per_combination()
    {
        var handler = new StubHttpHandler(_ => Page(1, hasNextPage: false,
            Item("NOVO11", "fund", "fiagro-novo"),
            Item("NOVA11", "fund", "fiagro-novo"),
            Item("BBAS3", "stock", "stock")));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        list.Assets.Count(a => a.Kind == AssetKind.Unknown).ShouldBe(2);
        var warning = _logs.Entries.Single(e => e.EventId.Id == 4002);
        warning.Level.ShouldBe(LogLevel.Warning);
        warning["ProviderSubType"].ShouldBe("fiagro-novo");
        warning["Count"].ShouldBe(2);
    }

    [Fact]
    public async Task Skips_invalid_and_duplicated_items_without_dropping_the_list()
    {
        var handler = new StubHttpHandler(request => StubHttpHandler.Query(request, "page") == "1"
            ? Page(1, hasNextPage: true,
                Item("BBAS3", "stock", "stock"),
                Item("", "stock", "stock"),
                Item("BBAS3.SA", "stock", "stock"),
                Item("ITSA4", "stock", "stock", name: null))
            : Page(2, hasNextPage: false,
                Item("bbas3", "stock", "stock"), // repetido de outra página, em minúsculas
                Item("MXRF11", "fund", "fii")));

        var list = await Source(handler).FetchAllAsync(CancellationToken.None);

        list.Assets.Select(a => a.Symbol).ShouldBe(["BBAS3", "MXRF11"]);
        list.Skipped.ShouldBe(4);
        _logs.Entries.Count(e => e.EventId.Id == 4001).ShouldBe(4);
    }

    [Fact]
    public async Task Logs_a_summary_of_the_fetch()
    {
        var handler = new StubHttpHandler(request =>
            StubHttpHandler.Fixture($"brapi-tickers-page-{StubHttpHandler.Query(request, "page")}.json"));

        await Source(handler).FetchAllAsync(CancellationToken.None);

        var summary = _logs.Entries.Single(e => e.EventId.Id == 4000);
        summary["Provider"].ShouldBe("brapi");
        summary["Assets"].ShouldBe(11);
        summary["Pages"].ShouldBe(2);
        summary["Skipped"].ShouldBe(0);
    }

    [Fact]
    public async Task Stops_at_the_page_ceiling_when_the_list_never_ends()
    {
        var handler = new StubHttpHandler(request =>
            Page(int.Parse(StubHttpHandler.Query(request, "page")!), hasNextPage: true, Item("BBAS3", "stock", "stock")));

        var error = await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler, new BrapiOptions { MaxPages = 3 }).FetchAllAsync(CancellationToken.None));

        error.Message.ShouldContain("3");
        handler.Requests.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Error_status_is_a_market_data_failure(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("{}", status));

        var error = await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAllAsync(CancellationToken.None));

        error.Message.ShouldContain(((int)status).ToString());
    }

    [Fact]
    public async Task Failure_on_a_later_page_returns_no_partial_list()
    {
        // Uma lista pela metade faria a sincronização dar por saídos da bolsa os ativos das páginas que faltaram.
        var handler = new StubHttpHandler(request => StubHttpHandler.Query(request, "page") == "1"
            ? StubHttpHandler.Fixture("brapi-tickers-page-1.json")
            : StubHttpHandler.Json("", HttpStatusCode.BadGateway));

        var error = await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAllAsync(CancellationToken.None));

        error.Message.ShouldContain("page 2");
        _logs.Entries.ShouldNotContain(e => e.EventId.Id == 4000);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"pagination":{"page":1,"hasNextPage":false}}""")] // sem results
    [InlineData("""{"results":[]}""")] // sem pagination
    [InlineData("""{"results":null,"pagination":{"page":1,"hasNextPage":false}}""")]
    [InlineData("null")]
    public async Task Unexpected_body_is_a_market_data_failure_not_an_empty_list(string body)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body));

        await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Network_failure_is_a_market_data_failure()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("connection refused"));

        var error = await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAllAsync(CancellationToken.None));

        error.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Slow_answer_times_out_as_a_market_data_failure()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpHandler.Fixture("brapi-tickers-page-1.json");
        });

        var error = await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler, timeout: TimeSpan.FromMilliseconds(100)).FetchAllAsync(CancellationToken.None));

        error.Message.ShouldContain("timed out");
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_a_failure()
    {
        // A API parando cancela a tarefa: isso não é falha da brapi e não deve ir ao log como tal.
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpHandler.Fixture("brapi-tickers-page-1.json");
        });

        await Should.ThrowAsync<OperationCanceledException>(() => Source(handler).FetchAllAsync(cancellation.Token));
    }

    private BrapiAssetListSource Source(StubHttpHandler handler, BrapiOptions? options = null, TimeSpan? timeout = null)
    {
        var settings = options ?? new BrapiOptions();
        var http = new HttpClient(handler)
        {
            BaseAddress = settings.BaseUrl,
            Timeout = timeout ?? TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };
        var loggers = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        return new BrapiAssetListSource(http, Options.Create(settings), loggers.CreateLogger<BrapiAssetListSource>());
    }

    private static object Item(string symbol, string? assetType, string? subType, string? name = "Nome") => new
    {
        symbol,
        name,
        longName = (string?)null,
        assetType,
        subType,
        exchange = "B3",
        currency = "BRL",
        isActive = true,
        logoUrl = (string?)null,
    };

    private static HttpResponseMessage Page(int page, bool hasNextPage, params object[] items) =>
        StubHttpHandler.Json(JsonSerializer.Serialize(new
        {
            results = items,
            indexes = Array.Empty<object>(),
            pagination = new { page, limit = 1000, totalItems = items.Length, totalPages = 1, hasNextPage },
        }));
}
