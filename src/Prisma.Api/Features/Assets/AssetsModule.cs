namespace Prisma.Api.Features.Assets;

// Catálogo de ativos da bolsa (docs/investimentos.md): a sincronização diária (lista e logos), a busca e o logo.
public static class AssetsModule
{
    public static IServiceCollection AddAssetFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssetSyncOptions>()
            .Bind(configuration.GetSection(AssetSyncOptions.Section))
            .Validate(o => o.HourOfDay is >= 0 and <= 23, "Assets:Sync:HourOfDay deve estar entre 0 e 23.")
            .ValidateOnStart();

        services.AddSingleton<AssetListSync>();
        services.AddSingleton<AssetLogoSync>();
        services.AddScoped<SearchAssets.Handler>();

        // Liga por padrão. Os testes de integração desligam: o catálogo é global, e cada teste chama a
        // sincronização quando quer.
        if (configuration.GetValue($"{AssetSyncOptions.Section}:{nameof(AssetSyncOptions.Enabled)}", defaultValue: true))
            services.AddHostedService<AssetListWorker>();
        return services;
    }

    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        SearchAssets.Map(app);
        GetAssetLogo.Map(app);
    }
}

public sealed class AssetSyncOptions
{
    public const string Section = "Assets:Sync";

    public bool Enabled { get; init; } = true;

    // Hora em São Paulo. 4h fica fora do pregão e longe do uso do app.
    public int HourOfDay { get; init; } = 4;
}
