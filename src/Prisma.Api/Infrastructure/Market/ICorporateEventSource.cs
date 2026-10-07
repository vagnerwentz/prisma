using System.Text.Json;
using System.Text.Json.Serialization;
using Prisma.Domain.Market;

namespace Prisma.Api.Infrastructure.Market;

// A curadoria dos eventos da empresa e dos ativos que saíram da bolsa antes do catálogo (docs/investimentos.md,
// I.7c1). Hoje, um arquivo dentro da imagem; uma tela de administração troca só esta fonte.
public interface ICorporateEventSource
{
    CorporateEventFile Load();
}

public sealed record CorporateEventFile(
    IReadOnlyList<HistoricalAssetEntry> HistoricalAssets, IReadOnlyList<CorporateEventEntry> Events);

public sealed record HistoricalAssetEntry(string Symbol, string Name, AssetKind Kind, DateOnly LastTradingDay);

// Target: só na conversão, o código do destino.
public sealed record CorporateEventEntry(
    string Key, CorporateEventKind Kind, string Symbol, string? Target, DateOnly Date, int From, int To, string Source);

public sealed class EmbeddedCorporateEventSource : ICorporateEventSource
{
    public const string ResourceName = "Prisma.Api.corporate-events.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public CorporateEventFile Load()
    {
        using var stream = typeof(EmbeddedCorporateEventSource).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Recurso {ResourceName} não está na imagem.");
        return JsonSerializer.Deserialize<CorporateEventFile>(stream, Json)
               ?? throw new InvalidOperationException("Arquivo de eventos vazio.");
    }
}
