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

// Target: na conversão e na liquidação, o código do destino. TargetUnitPrice e CashPerUnit: só na liquidação (I.7c4), em
// texto decimal com ponto e até 8 casas ("10.25"), como o fato relevante traz.
public sealed record CorporateEventEntry(
    string Key, CorporateEventKind Kind, string Symbol, string? Target, DateOnly Date, int From, int To, string Source,
    string? TargetUnitPrice = null, string? CashPerUnit = null);

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
