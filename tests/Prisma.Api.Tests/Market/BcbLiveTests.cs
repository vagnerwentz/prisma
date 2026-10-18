using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Prisma.Api.Infrastructure.Market.Bcb;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// Contrato com a API do Banco Central de verdade: confere que a resposta real ainda casa com o cliente e que as séries 12
// (diária) e 4389 (anualizada) seguem consistentes. Fica fora do `dotnet test` e do CI. Rode ao mexer no cliente ou
// desconfiar de mudança na API (usa 4 requisições, sem token):
//
//   PRISMA_LIVE_TESTS=1 dotnet test tests/Prisma.Api.Tests --filter Category=Live
[Trait("Category", "Live")]
public sealed class BcbLiveTests
{
    private static HttpClient Http() => new() { BaseAddress = new BcbOptions().BaseUrl, Timeout = TimeSpan.FromSeconds(30) };

    [LiveFact]
    public async Task The_real_series_still_matches_the_client()
    {
        using var http = Http();
        var source = new BcbSgsRateSource(http, NullLogger<BcbSgsRateSource>.Instance);

        // 11 anos: passa do limite de 10 da API, então o fatiamento do cliente é parte do teste.
        var from = new DateOnly(2015, 1, 1);
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        var rates = await source.FetchAsync(from, to, CancellationToken.None);

        // ~250 dias úteis por ano.
        rates.Count.ShouldBeGreaterThan(2_500);
        rates[0].Date.ShouldBe(new DateOnly(2015, 1, 2));
        rates[^1].Date.ShouldBeGreaterThan(to.AddDays(-12));
        rates.ShouldAllBe(r => r.RateE8 > 0 && r.RateE8 < RateHistory.MaxRateE8);
        RateHistory.Create(rates).IsSuccess.ShouldBeTrue();
    }

    // O CDI diário (12) é o anualizado (4389) em base 252: (1 + a/100)^(1/252) − 1 = d. O valor diário tem 6 casas, e o
    // anualizado 2, então a conta fecha dentro de 0,5 na sexta casa do diário.
    [LiveFact]
    public async Task The_daily_and_the_annualized_series_agree()
    {
        using var http = Http();
        var daily = await Read(http, 12);
        var annual = await Read(http, 4389);

        var day = daily.Keys.Intersect(annual.Keys).Max();
        var expected = (Math.Pow(1 + annual[day] / 100, 1.0 / 252) - 1) * 100;
        Math.Abs(daily[day] - expected).ShouldBeLessThan(0.00002, $"diária {daily[day]} contra {expected} (anualizada {annual[day]})");
    }

    [LiveFact]
    public async Task A_window_longer_than_ten_years_is_still_refused_by_the_api()
    {
        using var http = Http();

        using var response = await http.GetAsync(
            "dados/serie/bcdata.sgs.12/dados?formato=json&dataInicial=01/01/2000&dataFinal=31/12/2026");
        var body = await response.Content.ReadAsStringAsync();

        // Não importa o status (já veio 406 e 200): o corpo não pode ser uma lista de taxas.
        body.TrimStart().ShouldNotStartWith("[");
        body.ShouldContain("10 anos");
    }

    [LiveFact]
    public async Task A_weekend_range_still_answers_404_value_not_found()
    {
        using var http = Http();

        using var response = await http.GetAsync(
            "dados/serie/bcdata.sgs.12/dados?formato=json&dataInicial=17/10/2026&dataFinal=18/10/2026");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Value(s) not found");
    }

    private static async Task<Dictionary<DateOnly, double>> Read(HttpClient http, int series)
    {
        var from = DateTime.UtcNow.AddDays(-20).ToString("dd/MM/yyyy");
        using var response = await http.GetAsync($"dados/serie/bcdata.sgs.{series}/dados?formato=json&dataInicial={from}");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.EnumerateArray().ToDictionary(
            item => DateOnly.ParseExact(item.GetProperty("data").GetString()!, "dd/MM/yyyy"),
            item => double.Parse(item.GetProperty("valor").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }
}
