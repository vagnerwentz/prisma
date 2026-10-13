using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, "I.7e": a carteira em um ano, pelo custo, com os grupos e o que o ano rendeu.
[Collection(ApiCollection.Name)]
public sealed class PortfolioTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record GroupDto(string Group, long InvestedCents, int WeightBasisPoints, int AssetCount, long PayoutsCents, long RealizedCents);

    private sealed record AssetRowDto(
        Guid AssetId, string Symbol, string Name, string Kind, string Group, bool HasLogo, bool IsActive, long Quantity, long GrossCostCents,
        long CostBasisCents, long? AveragePriceCents, long? AverageCostCents, int WeightBasisPoints, long PayoutsCents, long RealizedCents);

    private sealed record PortfolioDto(
        int Year, string AsOf, List<int> Years, long InvestedCents, long PayoutsCents, long RealizedCents, List<GroupDto> Groups,
        List<AssetRowDto> Assets);

    private sealed record NoteDto(Guid Id, string Date);

    private sealed record ProblemDto(string? Detail);

    // 9 de outubro de 2026, meio-dia em São Paulo.
    private readonly FakeClock _clock = new(new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _source = new();

    private sealed record Assets(Guid Itub3, Guid Mxrf11, Guid Bbas3);

    private static object Note(string date, Guid assetId, string side, long quantity, long unitPriceCents, long feesCents = 0) =>
        new { broker = "XP", date, feesCents, brokerageFeeCents = 0, trades = new[] { new { assetId, side, quantity, unitPriceCents } } };

    // ITUB3 em 2024, MXRF11 e metade do ITUB3 vendida em 2025; rendimentos do MXRF11 em 2025 e 2026 (um ainda a cair).
    private static async Task OwnerYears(HttpClient client, Assets a)
    {
        await Created(client, "/trade-notes", Note("2024-03-10", a.Itub3, "Buy", 100, 2000));
        await Created(client, "/trade-notes", Note("2025-02-03", a.Mxrf11, "Buy", 100, 1000));
        await Created(client, "/trade-notes", Note("2025-08-01", a.Itub3, "Sell", 50, 2500));
        var ion = await Account(client);
        await Payout(client, ion, a.Mxrf11, 100, "2025-06-15");
        await Payout(client, ion, a.Mxrf11, 120, "2026-09-30");
        await Payout(client, ion, a.Mxrf11, 130, "2026-10-20");
    }

    [Fact]
    public async Task Past_year_is_the_portfolio_at_the_end_of_december() =>
        await WithClient(async (client, a) =>
        {
            await OwnerYears(client, a);

            var y2024 = await Portfolio(client, "?year=2024");

            (y2024.Year, y2024.AsOf, y2024.InvestedCents, y2024.PayoutsCents, y2024.RealizedCents).ShouldBe((2024, "2024-12-31", 200000L, 0L, 0L));
            y2024.Years.ShouldBe([2024, 2025, 2026]);
            y2024.Groups.ShouldBe([new GroupDto("Stocks", 200000, 10000, 1, 0, 0)]);
            y2024.Assets.Single().ShouldBe(new AssetRowDto(a.Itub3, "ITUB3", "ITAU UNIBANCO HOLDING S.A.", "Stock", "Stocks", false, true,
                100, 200000, 200000, 2000, 2000, 10000, 0, 0));
        });

    [Fact]
    public async Task Year_with_a_sale_and_payouts() =>
        await WithClient(async (client, a) =>
        {
            await OwnerYears(client, a);

            var y2025 = await Portfolio(client, "?year=2025");

            // 50 ITUB3 (R$ 1.000) e 100 MXRF11 (R$ 1.000): metade cada. Venda: 50 × 25 − 50 × 20 = R$ 250.
            (y2025.InvestedCents, y2025.PayoutsCents, y2025.RealizedCents).ShouldBe((200000L, 100L, 25000L));
            y2025.Groups.ShouldBe([new GroupDto("Stocks", 100000, 5000, 1, 0, 25000), new GroupDto("Fiis", 100000, 5000, 1, 100, 0)]);
            y2025.Assets.Select(x => (x.Symbol, x.Quantity, x.WeightBasisPoints, x.PayoutsCents, x.RealizedCents))
                .ShouldBe([("ITUB3", 50L, 5000, 0L, 25000L), ("MXRF11", 100L, 5000, 100L, 0L)]);
        });

    // Sem ano: o corrente, até hoje. O rendimento de 20/10 ainda não caiu.
    [Fact]
    public async Task Without_a_year_it_is_today() =>
        await WithClient(async (client, a) =>
        {
            await OwnerYears(client, a);

            var now = await Portfolio(client, "");

            (now.Year, now.AsOf, now.PayoutsCents, now.RealizedCents).ShouldBe((2026, "2026-10-09", 120L, 0L));
        });

    [Fact]
    public async Task Year_after_the_current_is_refused() =>
        await WithClient(async (client, _) =>
        {
            var response = await client.GetAsync("/portfolio?year=2027");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe("Escolha um ano até 2026.");
        });

    [Fact]
    public async Task Nothing_yet_offers_only_the_current_year() =>
        await WithClient(async (client, _) =>
        {
            var empty = await Portfolio(client, "");

            empty.Years.ShouldBe([2026]);
            (empty.InvestedCents, empty.Groups.Count, empty.Assets.Count).ShouldBe((0L, 0, 0));
        });

    // Provento antes da primeira nota também abre o ano dele; o ativo entra com peso zero.
    [Fact]
    public async Task Payout_before_any_note_opens_its_year() =>
        await WithClient(async (client, a) =>
        {
            await Payout(client, await Account(client), a.Bbas3, 500, "2022-04-01");

            var y2022 = await Portfolio(client, "?year=2022");

            y2022.Years.ShouldBe([2022, 2023, 2024, 2025, 2026]);
            y2022.Assets.Select(x => (x.Symbol, x.Quantity, x.WeightBasisPoints, x.PayoutsCents)).ShouldBe([("BBAS3", 0L, 0, 500L)]);
            y2022.Groups.ShouldBe([new GroupDto("Stocks", 0, 0, 0, 500, 0)]);
        });

    [Fact]
    public async Task Notes_of_one_year() =>
        await WithClient(async (client, a) =>
        {
            await OwnerYears(client, a);

            var notes = (await client.GetFromJsonAsync<List<NoteDto>>("/trade-notes?year=2025"))!;

            notes.Select(n => n.Date).ShouldBe(["2025-08-01", "2025-02-03"]);
            (await client.GetFromJsonAsync<List<NoteDto>>("/trade-notes"))!.Count.ShouldBe(3);
            (await client.GetFromJsonAsync<List<NoteDto>>("/trade-notes?year=2023"))!.ShouldBeEmpty();
        });

    [Fact]
    public async Task Each_person_sees_only_their_portfolio() =>
        await WithClient(async (client, a) =>
        {
            await OwnerYears(client, a);
            await using var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock);
            var other = await factory.CreateAuthenticatedClientAsync();

            var theirs = await Portfolio(other, "?year=2025");

            (theirs.InvestedCents, theirs.PayoutsCents, theirs.Assets.Count).ShouldBe((0L, 0L, 0));
            theirs.Years.ShouldBe([2026]);
        });

    [Fact]
    public async Task Login_is_required()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        (await factory.CreateHttpsClient().GetAsync("/portfolio")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task WithClient(Func<HttpClient, Assets, Task> test)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, null,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets =
        [
            Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A."),
            Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda"),
            Listed("BBAS3", AssetKind.Stock, "BANCO DO BRASIL S.A."),
        ];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var client = await factory.CreateAuthenticatedClientAsync();
        await test(client, new Assets(await AssetId(client, "ITUB3"), await AssetId(client, "MXRF11"), await AssetId(client, "BBAS3")));
    }

    private static async Task Created(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> Account(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/accounts", new { name = "Íon", type = "Investment", initialBalanceCents = 0 }))
            .Content.ReadFromJsonAsync<IdDto>())!.Id;

    private static Task Payout(HttpClient client, Guid accountId, Guid assetId, long amountCents, string date) =>
        Created(client, "/payouts", new { accountId, assetId, kind = "FundIncome", amountCents, date });

    private static async Task<PortfolioDto> Portfolio(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/portfolio{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortfolioDto>())!;
    }

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol).Id;
}
