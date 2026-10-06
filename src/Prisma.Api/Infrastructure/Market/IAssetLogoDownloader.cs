namespace Prisma.Api.Infrastructure.Market;

// Baixa o SVG de um logo pelo endereço que o fornecedor deu (docs/investimentos.md, etapa 4). Falha (fora do ar,
// lento, grande demais, não é SVG) é MarketDataException; quem chama segue para o próximo logo.
public interface IAssetLogoDownloader
{
    Task<string> DownloadAsync(string url, CancellationToken ct);
}

public sealed class HttpAssetLogoDownloader(HttpClient http) : IAssetLogoDownloader
{
    public const string HttpClientName = "asset-logos";

    public async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new MarketDataException($"logo: HTTP {(int)response.StatusCode}");

            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/svg+xml" or "text/xml" or "application/xml" or "text/plain"))
                throw new MarketDataException($"logo: unexpected content type {type}");

            if (response.Content.Headers.ContentLength > SvgSanitizer.MaxBytes)
                throw new MarketDataException("logo: too large");

            // Lê no máximo um byte além do limite: não baixa um arquivo enorme para descobrir que é enorme.
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[SvgSanitizer.MaxBytes + 1];
            var read = 0;
            int chunk;
            while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0)
                read += chunk;
            if (read > SvgSanitizer.MaxBytes)
                throw new MarketDataException("logo: too large");

            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (HttpRequestException ex)
        {
            throw new MarketDataException("logo: request failed", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MarketDataException("logo: timed out", ex);
        }
    }
}
