using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market.Brapi;

namespace Prisma.Api.Infrastructure.Market;

// Fornecedores de dados de mercado (docs/investimentos.md). Cada um é um HttpClient tipado do
// IHttpClientFactory, nomeado, com endereço, timeout e token vindos da configuração. Sem biblioteca de
// retentativa: a tarefa que usa roda uma vez por dia, e a próxima execução é a retentativa.
public static class MarketDataSetup
{
    public static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BrapiOptions>()
            .Bind(configuration.GetSection(BrapiOptions.Section))
            .Validate(o => o.IsValid(), "Configuração Brapi inválida: confira BaseUrl, TimeoutSeconds, PageSize e MaxPages.")
            .ValidateOnStart();

        services.AddHttpClient<IAssetListSource, BrapiAssetListSource>(BrapiAssetListSource.HttpClientName, (provider, client) =>
        {
            var brapi = provider.GetRequiredService<IOptions<BrapiOptions>>().Value;
            client.BaseAddress = brapi.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(brapi.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Prisma", "1.0"));
            // No cabeçalho, nunca na URL: a URL aparece nos logs do HttpClient.
            if (!string.IsNullOrWhiteSpace(brapi.Token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", brapi.Token);
        });

        // Logos (etapa 4): só baixa arquivo pequeno; um logo lento não segura os outros.
        services.AddHttpClient<IAssetLogoDownloader, HttpAssetLogoDownloader>(HttpAssetLogoDownloader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Prisma", "1.0"));
        });

        return services;
    }
}
