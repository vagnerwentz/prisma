using System.Collections.Concurrent;
using Prisma.Api.Infrastructure.Market;

namespace Prisma.Api.Tests.Market;

// Logos sob controle do teste: endereço → SVG; endereço que não está no mapa falha como o fornecedor fora do ar.
public sealed class FakeAssetLogoDownloader : IAssetLogoDownloader
{
    private readonly ConcurrentQueue<string> _requested = new();

    public ConcurrentDictionary<string, string> Svgs { get; } = new();

    public IReadOnlyCollection<string> Requested => _requested.ToArray();

    public Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        _requested.Enqueue(url);
        return Svgs.TryGetValue(url, out var svg)
            ? Task.FromResult(svg)
            : Task.FromException<string>(new MarketDataException("logo: HTTP 404"));
    }

    public static string Square(string color) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="56" height="56"><path fill="{color}" d="M0 0h56v56H0z"/></svg>""";
}
