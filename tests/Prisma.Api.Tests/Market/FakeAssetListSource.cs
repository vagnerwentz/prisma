using Prisma.Api.Infrastructure.Market;
using Prisma.Domain.Market;

namespace Prisma.Api.Tests.Market;

// Fonte da lista de ativos sob controle do teste: devolve a lista do momento ou lança a falha marcada.
public sealed class FakeAssetListSource : IAssetListSource
{
    private int _calls;

    public IReadOnlyList<ListedAsset> Assets { get; set; } = [];

    public Exception? Failure { get; set; }

    public int Calls => _calls;

    public Task<AssetList> FetchAllAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Failure is not null
            ? Task.FromException<AssetList>(Failure)
            : Task.FromResult(new AssetList(Assets, 1, 0));
    }

    public static ListedAsset Listed(
        string symbol, AssetKind kind = AssetKind.Stock, string? name = null, string? longName = null, string? logoUrl = null)
    {
        if (!ListedAsset.TryCreate(symbol, name ?? $"Empresa {symbol}", longName, kind, out var asset, logoUrl))
            throw new ArgumentException($"Código inválido no teste: {symbol}");
        return asset;
    }
}
