using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Market.Brapi;
using Prisma.Domain.Market;
using Shouldly;

namespace Prisma.Api.Tests.Market;

// Contrato com a brapi de verdade: confere que a resposta real ainda casa com o que o cliente espera
// (formato, tipos, paginação). Fica fora do `dotnet test` e do CI, que não podem depender da internet nem
// do humor da brapi. Rode quando mexer no cliente ou desconfiar de mudança na brapi:
//
//   PRISMA_LIVE_TESTS=1 dotnet test tests/Prisma.Api.Tests --filter Category=Live
[Trait("Category", "Live")]
public sealed class BrapiLiveTests
{
    [LiveFact]
    public async Task The_real_list_still_matches_the_client()
    {
        var options = new BrapiOptions();
        using var http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        var source = new BrapiAssetListSource(http, Options.Create(options), NullLogger<BrapiAssetListSource>.Instance);

        var list = await source.FetchAllAsync(CancellationToken.None);

        // 2.337 em 2026-10-03: a folga cobre o vaivém normal; uma queda grande é sinal de mudança.
        list.Assets.Count.ShouldBeGreaterThan(1500);
        list.Pages.ShouldBeGreaterThan(1);
        list.Skipped.ShouldBe(0);
        list.Assets.ShouldNotContain(a => a.Kind == AssetKind.Unknown, "a brapi tem um tipo novo: ensine o BrapiAssetKinds");

        var bySymbol = list.Assets.ToDictionary(a => a.Symbol);
        bySymbol["BBAS3"].Kind.ShouldBe(AssetKind.Stock);
        bySymbol["TAEE11"].Kind.ShouldBe(AssetKind.Unit);
        bySymbol["MXRF11"].Kind.ShouldBe(AssetKind.Fii);
        bySymbol["MXRF11"].LongName.ShouldNotBeNull();
        bySymbol["BOVA11"].Kind.ShouldBe(AssetKind.Etf);
    }
}

// Roda só com PRISMA_LIVE_TESTS=1; sem a variável, aparece como ignorado, com o motivo.
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PRISMA_LIVE_TESTS") != "1")
            Skip = "Chama serviço externo: rode com PRISMA_LIVE_TESTS=1.";
    }
}
