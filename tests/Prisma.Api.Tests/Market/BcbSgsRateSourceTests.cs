using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Market.Bcb;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// Cliente do CDI diário do Banco Central, SGS 12 (docs/renda-fixa.md, seção 3). As fixtures são respostas reais da API
// (2026-10-17). O que ficou comprovado nelas: a API devolve o erro de janela com HTTP 406 ou com 200 (as duas coisas
// aconteceram na mesma consulta), faixa sem dia útil responde 404 e não lista vazia, e o valor vem como texto.
public sealed class BcbSgsRateSourceTests
{
    private static readonly DateOnly From = new(2026, 7, 21);
    private static readonly DateOnly To = new(2026, 10, 17);

    private readonly LogSink _logs = new();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Market", "Fixtures", name));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        StubHttpHandler.Json(body, status);

    private static HttpResponseMessage Window() => Json(Fixture("bcb-sgs12-window.json"));

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy");

    // ---- A resposta boa ----

    [Fact]
    public async Task Reads_the_real_window_as_daily_rates_in_date_order()
    {
        var rates = await Source(new StubHttpHandler(_ => Window())).FetchAsync(From, To, CancellationToken.None);

        rates.Count.ShouldBe(61);
        rates[0].ShouldBe(new DailyRate(new DateOnly(2026, 7, 21), 52_531));
        rates[^1].ShouldBe(new DailyRate(new DateOnly(2026, 10, 15), 50_788));
        rates.Select(r => r.Date).ShouldBe(rates.Select(r => r.Date).Order());
        // 12/10 é feriado e 10 e 11 são fim de semana: a série não tem esses dias, e o cliente não inventa.
        rates.ShouldNotContain(r => r.Date == new DateOnly(2026, 10, 12));
        rates.ShouldNotContain(r => r.Date == new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task Asks_for_the_series_with_the_dates_in_the_bcb_format()
    {
        var handler = new StubHttpHandler(_ => Window());

        await Source(handler).FetchAsync(From, To, CancellationToken.None);

        var request = handler.Requests.Single();
        request.RequestUri!.AbsolutePath.ShouldBe("/dados/serie/bcdata.sgs.12/dados");
        StubHttpHandler.Query(request, "formato").ShouldBe("json");
        StubHttpHandler.Query(request, "dataInicial").ShouldBe("21/07/2026");
        StubHttpHandler.Query(request, "dataFinal").ShouldBe("17/10/2026");
    }

    // Os valores são texto com ponto: "0.050788" é 0,050788 % ao dia, 50788 em 10⁻⁸. Nunca passam por double.
    [Theory]
    [InlineData("0.050788", 50_788)]
    [InlineData("0.052531", 52_531)]
    [InlineData("0.04", 40_000)]
    [InlineData("0.000001", 1)]
    [InlineData("0.999999", 999_999)]
    [InlineData("0.5", 500_000)]
    public async Task Reads_the_rate_text_exactly(string text, long expectedE8)
    {
        var handler = new StubHttpHandler(_ => Json($$"""[{"data":"13/10/2026","valor":"{{text}}"}]"""));

        var rates = await Source(handler).FetchAsync(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13), CancellationToken.None);

        rates.Single().RateE8.ShouldBe(expectedE8);
    }

    [Fact]
    public async Task A_range_with_no_business_day_is_an_empty_list_not_a_failure()
    {
        // Resposta real: 17 e 18/10/2026 são sábado e domingo; a API responde 404, não [].
        var handler = new StubHttpHandler(_ => Json(Fixture("bcb-empty.json"), HttpStatusCode.NotFound));

        var rates = await Source(handler).FetchAsync(new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 18), CancellationToken.None);

        rates.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_array_is_an_empty_list()
    {
        var rates = await Source(new StubHttpHandler(_ => Json("[]"))).FetchAsync(From, To, CancellationToken.None);

        rates.ShouldBeEmpty();
    }

    // ---- Janela de 10 anos ----

    // A API recusa janela maior que 10 anos em série diária: o cliente fatia em pedaços de 9 anos, sem buraco nem sobreposição.
    [Fact]
    public async Task A_range_longer_than_the_bcb_window_is_split_into_contiguous_windows()
    {
        var handler = new StubHttpHandler(request =>
        {
            var start = DateOnly.ParseExact(StubHttpHandler.Query(request, "dataInicial")!, "dd/MM/yyyy");
            return Json($$"""[{"data":"{{Date(start)}}","valor":"0.050000"}]""");
        });

        var rates = await Source(handler).FetchAsync(new DateOnly(1998, 1, 1), new DateOnly(2026, 10, 16), CancellationToken.None);

        var windows = handler.Requests
            .Select(r => (
                Start: DateOnly.ParseExact(StubHttpHandler.Query(r, "dataInicial")!, "dd/MM/yyyy"),
                End: DateOnly.ParseExact(StubHttpHandler.Query(r, "dataFinal")!, "dd/MM/yyyy")))
            .ToList();
        windows.First().Start.ShouldBe(new DateOnly(1998, 1, 1));
        windows.Last().End.ShouldBe(new DateOnly(2026, 10, 16));
        windows.Count.ShouldBe(4);
        windows.ShouldAllBe(w => w.End.DayNumber - w.Start.DayNumber < 9 * 366);
        windows.Zip(windows.Skip(1)).ShouldAllBe(pair => pair.Second.Start == pair.First.End.AddDays(1));
        rates.Count.ShouldBe(4);
        rates.Select(r => r.Date).ShouldBe(rates.Select(r => r.Date).Order());
    }

    [Fact]
    public async Task A_range_of_nine_years_is_one_request()
    {
        var handler = new StubHttpHandler(_ => Json("[]"));

        await Source(handler).FetchAsync(new DateOnly(2017, 10, 17), new DateOnly(2026, 10, 16), CancellationToken.None);

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_in_a_later_window_returns_nothing()
    {
        var handler = new StubHttpHandler(request => StubHttpHandler.Query(request, "dataInicial") == "01/01/2000"
            ? Json("""[{"data":"03/01/2000","valor":"0.050000"}]""")
            : Json("", HttpStatusCode.BadGateway));

        await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler).FetchAsync(new DateOnly(2000, 1, 1), new DateOnly(2026, 10, 16), CancellationToken.None));
        _logs.Entries.ShouldNotContain(e => e.EventId.Id == 4200);
    }

    [Fact]
    public async Task A_start_after_the_end_is_a_programming_error()
    {
        var handler = new StubHttpHandler(_ => Json("[]"));

        await Should.ThrowAsync<ArgumentException>(() => Source(handler).FetchAsync(To, From, CancellationToken.None));
        handler.Requests.ShouldBeEmpty();
    }

    // ---- A API respondeu, mas não é a série (o status não prova nada) ----

    // Resposta real: a mesma consulta sem dataInicial já voltou 406 e já voltou 200, com o mesmo corpo de erro.
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotAcceptable)]
    public async Task The_window_error_is_a_failure_whatever_the_status(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => Json(Fixture("bcb-window-too-long.json"), status));

        var error = await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAsync(From, To, CancellationToken.None));

        error.Message.ShouldContain("sgs 12");
    }

    // Já houve 200 com uma página HTML de "Requisição inválida" no lugar do JSON.
    [Fact]
    public async Task An_html_page_with_status_200_is_a_failure()
    {
        var html = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Requisição inválida</body></html>", Encoding.UTF8, "text/html"),
        };

        var error = await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => html)).FetchAsync(From, To, CancellationToken.None));

        error.Message.ShouldContain("content type");
    }

    [Fact]
    public async Task Html_declared_as_json_is_a_failure_too() =>
        await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json("<html><body>Requisição inválida</body></html>"))).FetchAsync(From, To, CancellationToken.None));

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Error_status_is_a_market_data_failure(HttpStatusCode status)
    {
        var error = await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json("{}", status))).FetchAsync(From, To, CancellationToken.None));

        error.Message.ShouldContain(((int)status).ToString());
    }

    // 404 que não é "sem valores": a série ou o endereço estão errados, e isso não pode virar "sem taxas hoje".
    [Fact]
    public async Task A_404_that_is_not_the_empty_range_is_a_failure() =>
        await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json("""{"erro":{"statusCode":404,"detail":"Serie not found"}}""", HttpStatusCode.NotFound)))
                .FetchAsync(From, To, CancellationToken.None));

    [Theory]
    [InlineData("não é json")]
    [InlineData("null")]
    [InlineData("""{"data":"13/10/2026","valor":"0.050788"}""")] // objeto, não lista
    [InlineData("""[{"data":"13/10/2026"}]""")] // sem valor
    [InlineData("""[{"valor":"0.050788"}]""")] // sem data
    [InlineData("""[{"data":"13/10/2026","valor":0.050788}]""")] // número, não texto
    [InlineData("""[{"data":null,"valor":"0.050788"}]""")]
    [InlineData("""[{"data":"13/10/2026","valor":null}]""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""[{"data":"2026-10-13","valor":"0.050788"}]""")] // data em outro formato
    [InlineData("""[{"data":"31/02/2026","valor":"0.050788"}]""")] // data que não existe
    public async Task A_body_that_is_not_the_series_is_a_failure_not_an_empty_list(string body)
    {
        await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json(body))).FetchAsync(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13), CancellationToken.None));
    }

    // Nunca zero no lugar de taxa ruim: valor estranho recusa o lote inteiro.
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0,050788")] // vírgula
    [InlineData("-0.050788")]
    [InlineData("0")]
    [InlineData("0.000000")]
    [InlineData("1.000000")] // 1 % ao dia é taxa impossível
    [InlineData("13.65")] // é a anualizada (SGS 4389), não a diária
    [InlineData("0.0507881")] // 7 casas: perderia precisão
    [InlineData("1e-3")]
    [InlineData(" 0.050788")]
    [InlineData(".5")]
    [InlineData("5.")]
    public async Task A_rate_value_that_is_not_a_daily_rate_is_a_failure(string value)
    {
        var handler = new StubHttpHandler(_ => Json($$"""[{"data":"13/10/2026","valor":"{{value}}"}]"""));

        await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler).FetchAsync(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13), CancellationToken.None));
    }

    [Fact]
    public async Task A_repeated_date_is_a_failure()
    {
        const string body = """[{"data":"13/10/2026","valor":"0.050788"},{"data":"13/10/2026","valor":"0.050788"}]""";

        await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json(body))).FetchAsync(From, To, CancellationToken.None));
    }

    [Fact]
    public async Task Dates_out_of_order_are_a_failure()
    {
        const string body = """[{"data":"14/10/2026","valor":"0.050788"},{"data":"13/10/2026","valor":"0.050788"}]""";

        await Should.ThrowAsync<MarketDataException>(() =>
            Source(new StubHttpHandler(_ => Json(body))).FetchAsync(From, To, CancellationToken.None));
    }

    [Fact]
    public async Task A_date_outside_the_requested_range_is_a_failure()
    {
        var handler = new StubHttpHandler(_ => Json("""[{"data":"13/10/2026","valor":"0.050788"}]"""));

        await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler).FetchAsync(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 16), CancellationToken.None));
    }

    // ---- Rede ----

    [Fact]
    public async Task Network_failure_is_a_market_data_failure()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("connection refused"));

        var error = await Should.ThrowAsync<MarketDataException>(() => Source(handler).FetchAsync(From, To, CancellationToken.None));

        error.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Slow_answer_times_out_as_a_market_data_failure()
    {
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Window();
        });

        var error = await Should.ThrowAsync<MarketDataException>(() =>
            Source(handler, timeout: TimeSpan.FromMilliseconds(100)).FetchAsync(From, To, CancellationToken.None));

        error.Message.ShouldContain("timed out");
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_a_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Window();
        });

        await Should.ThrowAsync<OperationCanceledException>(() => Source(handler).FetchAsync(From, To, cancellation.Token));
    }

    // ---- Log ----

    [Fact]
    public async Task Logs_a_summary_without_any_rate_value()
    {
        await Source(new StubHttpHandler(_ => Window())).FetchAsync(From, To, CancellationToken.None);

        var summary = _logs.Entries.Single(e => e.EventId.Id == 4200);
        summary.Level.ShouldBe(LogLevel.Information);
        summary["Provider"].ShouldBe("bcb-sgs-12");
        summary["Rates"].ShouldBe(61);
        summary["Windows"].ShouldBe(1);
        summary.Message.ShouldNotContain("0.05");
    }

    private BcbSgsRateSource Source(StubHttpHandler handler, BcbOptions? options = null, TimeSpan? timeout = null)
    {
        var settings = options ?? new BcbOptions();
        var http = new HttpClient(handler)
        {
            BaseAddress = settings.BaseUrl,
            Timeout = timeout ?? TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };
        var loggers = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        return new BcbSgsRateSource(http, loggers.CreateLogger<BcbSgsRateSource>());
    }
}
