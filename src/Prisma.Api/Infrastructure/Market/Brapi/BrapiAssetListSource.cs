using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Market.Brapi;

// Lista de ativos da B3 pela brapi (GET /api/v2/tickers; docs/investimentos.md, seções 2 e 4).
//
// Percorre as páginas ordenadas por código: a ordem padrão é por volume, que muda entre uma página e
// outra e faria um ativo aparecer duas vezes ou nenhuma. Devolve a lista inteira ou lança
// MarketDataException; nunca uma lista pela metade, que faria a sincronização dar por "saídos da bolsa"
// os ativos das páginas que faltaram.
public sealed class BrapiAssetListSource(
    HttpClient http, IOptions<BrapiOptions> options, ILogger<BrapiAssetListSource> logger) : IAssetListSource
{
    public const string HttpClientName = "brapi";
    private const string Provider = "brapi";

    // RespectNullableAnnotations e RespectRequiredConstructorParameters: resposta sem `results` ou sem
    // `pagination` é JsonException (resposta inválida), não uma lista vazia que passaria por verdadeira.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public async Task<AssetList> FetchAllAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var elapsed = Stopwatch.StartNew();
        var assets = new List<ListedAsset>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var unknownKinds = new Dictionary<(string? AssetType, string? SubType), int>();
        var skipped = 0;
        var pages = 0;

        while (true)
        {
            pages++;
            var page = await FetchPageAsync(pages, settings.PageSize, ct);

            foreach (var item in page.Results)
            {
                var kind = BrapiAssetKinds.From(item.AssetType, item.SubType);
                if (kind == AssetKind.Unknown)
                    unknownKinds[(item.AssetType, item.SubType)] = unknownKinds.GetValueOrDefault((item.AssetType, item.SubType)) + 1;

                if (!ListedAsset.TryCreate(item.Symbol, item.Name, item.LongName, kind, out var asset, LogoOf(item)))
                {
                    skipped++;
                    logger.AssetListItemSkipped(Provider, item.Symbol, "invalid symbol or missing name");
                }
                else if (!symbols.Add(asset.Symbol))
                {
                    skipped++;
                    logger.AssetListItemSkipped(Provider, asset.Symbol, "duplicated symbol");
                }
                else
                {
                    assets.Add(asset);
                }
            }

            if (!page.Pagination.HasNextPage)
                break;
            if (pages >= settings.MaxPages)
                throw new MarketDataException(
                    $"brapi tickers: still more pages after {settings.MaxPages}; raise Brapi:MaxPages if the list really grew");
        }

        foreach (var ((assetType, subType), count) in unknownKinds)
            logger.AssetKindUnknown(Provider, assetType, subType, count);
        logger.AssetListFetched(Provider, assets.Count, pages, skipped, elapsed.Elapsed.TotalMilliseconds);

        return new AssetList(assets, pages, skipped);
    }

    // Sem logo próprio, a brapi aponta para o dela (BRAPI.svg): para nós, é "sem logo", e o ativo mostra o código.
    private static string? LogoOf(BrapiTicker item) =>
        item.LogoUrl is { } url && !url.EndsWith("/BRAPI.svg", StringComparison.OrdinalIgnoreCase) ? url : null;

    private async Task<BrapiTickerPage> FetchPageAsync(int page, int pageSize, CancellationToken ct)
    {
        var uri = $"api/v2/tickers?page={page}&limit={pageSize}&sortBy=symbol&sortOrder=asc";
        try
        {
            using var response = await http.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
                throw new MarketDataException($"brapi tickers page {page}: HTTP {(int)response.StatusCode}");

            return await response.Content.ReadFromJsonAsync<BrapiTickerPage>(Json, ct)
                ?? throw new MarketDataException($"brapi tickers page {page}: empty body");
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException($"brapi tickers page {page}: request failed", ex);
        }
        catch (JsonException ex)
        {
            throw new MarketDataException($"brapi tickers page {page}: unexpected response format", ex);
        }
        // O HttpClient sinaliza o próprio timeout como cancelamento; o cancelamento de quem chamou passa adiante.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MarketDataException($"brapi tickers page {page}: timed out", ex);
        }
    }
}
