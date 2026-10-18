using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain.FixedIncome;

namespace Prisma.Api.Infrastructure.Market.Bcb;

// CDI diário pela SGS 12 do Banco Central (docs/renda-fixa.md, seção 3): % ao dia, 6 casas, só dias úteis, com o valor
// como texto ("0.050788"). Medido em 2026-10-17, e é por isso que este cliente desconfia do status:
//
//  - A janela máxima é de 10 anos em série diária. Mais que isso dá erro, e o erro já veio com HTTP 406 e com HTTP 200 na
//    mesma consulta (uma vez veio até uma página HTML). Por isso o pedido é fatiado em janelas de 9 anos e a resposta
//    só vale se for uma lista JSON; status e corpo de erro nunca viram "sem taxas".
//  - Faixa sem nenhum dia útil responde 404 ("Value(s) not found"), não lista vazia. Isso é o caso normal de uma
//    sincronização num fim de semana, e vira lista vazia. Qualquer outro 404 é falha.
//
// Devolve tudo ou nada: se uma janela falha, a falha sobe e nenhuma taxa é devolvida pela metade.
public sealed class BcbSgsRateSource(HttpClient http, ILogger<BcbSgsRateSource> logger) : IReferenceRateSource
{
    public const string HttpClientName = "bcb-sgs";

    public const int SeriesCode = 12;

    public const string Provider = "bcb-sgs-12";

    // Abaixo do limite de 10 anos da API, com folga para ano bissexto e para mudança de regra.
    private const int WindowYears = 9;

    private const string DateFormat = "dd/MM/yyyy";

    private const string NoValuesMarker = "Value(s) not found";

    public async Task<IReadOnlyList<DailyRate>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);

        var elapsed = Stopwatch.StartNew();
        var rates = new List<DailyRate>();
        var windows = 0;
        for (var start = from; start <= to;)
        {
            var end = DateOnly.FromDayNumber(Math.Min(to.DayNumber, start.AddYears(WindowYears).AddDays(-1).DayNumber));
            rates.AddRange(await FetchWindowAsync(start, end, ct));
            windows++;
            start = end.AddDays(1);
        }

        // As janelas se emendam: a lista inteira tem de continuar em ordem, sem data repetida e com taxas plausíveis.
        var checkedRates = RateHistory.Create(rates);
        if (!checkedRates.IsSuccess)
            throw new MarketDataException($"bcb sgs {SeriesCode}: {checkedRates.Error.Message}");

        logger.ReferenceRatesFetched(Provider, rates.Count, windows, from, to, elapsed.Elapsed.TotalMilliseconds);
        return rates;
    }

    private async Task<List<DailyRate>> FetchWindowAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var range = $"{Format(start)}-{Format(end)}";
        var uri = $"dados/serie/bcdata.sgs.{SeriesCode}/dados?formato=json&dataInicial={Format(start)}&dataFinal={Format(end)}";
        try
        {
            using var response = await http.GetAsync(uri, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.NotFound && body.Contains(NoValuesMarker, StringComparison.Ordinal))
                return [];
            if (!response.IsSuccessStatusCode)
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: unexpected content type '{mediaType}'");

            return Parse(body, start, end, range);
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException($"bcb sgs {SeriesCode} {range}: request failed", ex);
        }
        catch (JsonException ex)
        {
            throw new MarketDataException($"bcb sgs {SeriesCode} {range}: unexpected response format", ex);
        }
        // O HttpClient sinaliza o próprio timeout como cancelamento; o cancelamento de quem chamou passa adiante.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MarketDataException($"bcb sgs {SeriesCode} {range}: timed out", ex);
        }
    }

    private static List<DailyRate> Parse(string body, DateOnly start, DateOnly end, string range)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new MarketDataException($"bcb sgs {SeriesCode} {range}: response is not a list");

        var rates = new List<DailyRate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryText(item, "data", out var dateText)
                || !TryText(item, "valor", out var valueText))
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: item without date and value as text");

            if (!DateOnly.TryParseExact(dateText, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: invalid date");
            if (date < start || date > end)
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: date {Format(date)} outside the requested range");
            if (!TryParseRateE8(valueText, out var rateE8))
                throw new MarketDataException($"bcb sgs {SeriesCode} {range}: invalid rate on {Format(date)}");

            rates.Add(new DailyRate(date, rateE8));
        }

        return rates;
    }

    private static bool TryText(JsonElement item, string name, out string text)
    {
        text = "";
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        text = property.GetString()!;
        return true;
    }

    // "0.050788" → 50788 (fração em 10⁻⁸ = % ao dia × 10⁶). À mão, em inteiro: o texto nunca passa por double. Aceita só
    // dígitos, ponto e até 6 casas: mais casas perderiam precisão. O limite da taxa (RateHistory) é conferido depois.
    internal static bool TryParseRateE8(string text, out long rateE8)
    {
        rateE8 = 0;
        var parts = text.Split('.');
        if (parts.Length > 2 || parts[0].Length is 0 or > 3 || !parts[0].All(char.IsAsciiDigit))
            return false;

        var fraction = parts.Length == 2 ? parts[1] : "";
        if (parts.Length == 2 && (fraction.Length is 0 or > 6 || !fraction.All(char.IsAsciiDigit)))
            return false;

        rateE8 = long.Parse(parts[0], CultureInfo.InvariantCulture) * 1_000_000
            + (fraction.Length == 0 ? 0 : long.Parse(fraction.PadRight(6, '0'), CultureInfo.InvariantCulture));
        return true;
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
