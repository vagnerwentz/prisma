using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.FixedIncome;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/renda-fixa.md, R.9: a renda fixa dentro da carteira (GET /portfolio): um grupo só, pelo aportado, uma linha por objetivo, o
// rendimento bruto ao lado e os pesos sobre o total. Os valores esperados saem dos mesmos saldos reais de FixedIncomeBalancesApiTests
// (taxas do Banco Central de 21/07 a 15/10/2026; cálculo independente).
[Collection(ApiCollection.Name)]
public sealed class PortfolioFixedIncomeTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record GroupDto(string Group, long InvestedCents, int WeightBasisPoints, int AssetCount);

    private sealed record AssetRowDto(string Symbol, long CostBasisCents, int WeightBasisPoints);

    private sealed record LineDto(Guid? GoalId, string? Name, int DepositCount, long PrincipalCents, long GrossYieldCents, int WeightBasisPoints);

    private sealed record PortfolioDto(
        int Year, DateOnly AsOf, List<int> Years, long InvestedCents, List<GroupDto> Groups, List<AssetRowDto> Assets, List<LineDto> FixedIncome,
        DateOnly? FixedIncomeRatesThrough);

    // Segunda-feira, 19/10/2026, 12h em São Paulo: a última taxa gravada é a de 15/10.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Wedding { get; set; }
        public Guid Trip { get; set; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // ---- O grupo, as linhas e os pesos ----

    [Fact]
    public async Task Fixed_income_is_one_group_with_a_line_per_goal_and_the_loose_deposits()
    {
        await using var s = await Start();

        var portfolio = await Portfolio(s.Client, "");

        // Aportado: casamento 609.731, viagem 1.947.408 e soltos 100.000.
        const long total = 609_731 + 1_947_408 + 100_000;
        portfolio.InvestedCents.ShouldBe(total);
        portfolio.Groups.ShouldHaveSingleItem().ShouldBe(new GroupDto("FixedIncome", total, 10_000, 3));
        portfolio.FixedIncome.Select(l => (l.Name, l.DepositCount, l.PrincipalCents, l.GrossYieldCents)).ToList().ShouldBe(
            [("Viagem", 2, 1_947_408L, 54_833L), ("Casamento", 2, 609_731L, 19_471L), (null, 1, 100_000L, 51L)]);
        portfolio.FixedIncome.Sum(l => l.WeightBasisPoints).ShouldBe(10_000);
        portfolio.FixedIncomeRatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
    }

    [Fact]
    public async Task The_line_weights_are_the_share_of_the_principal()
    {
        await using var s = await Start();

        var lines = (await Portfolio(s.Client, "")).FixedIncome;

        // 1.947.408 / 2.657.139 = 73,29%; 609.731 = 22,95%; 100.000 = 3,76%.
        lines.Select(l => l.WeightBasisPoints).ToList().ShouldBe([7_329, 2_295, 376]);
    }

    [Fact]
    public async Task The_group_comes_with_the_stock_portfolio_and_changes_every_weight()
    {
        await using var s = await Start(withStocks: true);

        var portfolio = await Portfolio(s.Client, "");

        // Ações: 100 × R$ 20,00 = R$ 2.000,00 (200.000). Total 2.657.139 + 200.000 = 2.857.139.
        const long fixedIncome = 609_731 + 1_947_408 + 100_000;
        portfolio.InvestedCents.ShouldBe(fixedIncome + 200_000);
        portfolio.Groups.Select(g => (g.Group, g.InvestedCents)).ToList().ShouldBe([("FixedIncome", fixedIncome), ("Stocks", 200_000L)]);
        // 200.000 / 2.857.139 = 7,0%.
        portfolio.Assets.Single().WeightBasisPoints.ShouldBe(700);
        portfolio.Groups.Sum(g => g.WeightBasisPoints).ShouldBe(10_000);
        (portfolio.Assets.Sum(a => a.WeightBasisPoints) + portfolio.FixedIncome.Sum(l => l.WeightBasisPoints)).ShouldBe(10_000);
    }

    // ---- Por ano ----

    [Fact]
    public async Task A_year_before_the_first_deposit_has_no_fixed_income()
    {
        await using var s = await Start();

        var portfolio = await Portfolio(s.Client, "?year=2025");

        portfolio.FixedIncome.ShouldBeEmpty();
        portfolio.Groups.ShouldBeEmpty();
        portfolio.InvestedCents.ShouldBe(0);
        portfolio.FixedIncomeRatesThrough.ShouldBeNull();
    }

    [Fact]
    public async Task Each_year_counts_only_the_deposits_up_to_its_end_and_the_years_start_at_the_first_deposit()
    {
        await using var s = await Start();
        // Uma taxa antiga, só para aceitar um aporte de 2024 (o rendimento dele só existe onde há taxa).
        await Seed(s, extra: [new DailyRate(new DateOnly(2024, 1, 2), 40_000)]);
        await Deposit(s.Client, s.Trip, 50_000, new DateOnly(2024, 6, 3));

        var y2024 = await Portfolio(s.Client, "?year=2024");
        var y2025 = await Portfolio(s.Client, "?year=2025");
        var y2023 = await Portfolio(s.Client, "?year=2023");

        y2024.Years.ShouldBe([2024, 2025, 2026]);
        y2024.AsOf.ShouldBe(new DateOnly(2024, 12, 31));
        y2024.FixedIncome.ShouldHaveSingleItem().PrincipalCents.ShouldBe(50_000);
        y2025.FixedIncome.ShouldHaveSingleItem().PrincipalCents.ShouldBe(50_000);
        y2025.FixedIncome.Single().WeightBasisPoints.ShouldBe(10_000);
        y2023.FixedIncome.ShouldBeEmpty();
        (await Portfolio(s.Client, "")).FixedIncome.Sum(l => l.PrincipalCents).ShouldBe(609_731 + 1_947_408 + 100_000 + 50_000);
    }

    // Aporte sem taxa não some da carteira: entra pelo aportado, com rendimento zero.
    [Fact]
    public async Task A_deposit_without_a_rate_still_counts_by_its_principal_with_zero_yield()
    {
        await using var s = await Start();
        await ClearRates(s);

        var portfolio = await Portfolio(s.Client, "");

        portfolio.InvestedCents.ShouldBe(609_731 + 1_947_408 + 100_000);
        portfolio.FixedIncome.ShouldAllBe(l => l.GrossYieldCents == 0);
    }

    [Fact]
    public async Task A_goal_with_no_deposits_does_not_appear()
    {
        await using var s = await Start();
        await Goal(s.Client, "Vazio");

        var names = (await Portfolio(s.Client, "")).FixedIncome.Select(l => l.Name);

        names.ShouldNotContain("Vazio");
    }

    [Fact]
    public async Task Deleting_a_deposit_takes_it_out_of_the_portfolio()
    {
        await using var s = await Start();
        var deposits = await s.Client.GetFromJsonAsync<List<IdDto>>($"/fixed-income-deposits?goal={s.Wedding}");
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposits![0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var portfolio = await Portfolio(s.Client, "");

        portfolio.FixedIncome.Single(l => l.Name == "Casamento").DepositCount.ShouldBe(1);
    }

    [Fact]
    public async Task One_user_never_sees_the_fixed_income_of_another()
    {
        await using var s = await Start();
        using var other = await s.Factory.CreateAuthenticatedClientAsync();

        var portfolio = await Portfolio(other, "");

        portfolio.FixedIncome.ShouldBeEmpty();
        portfolio.InvestedCents.ShouldBe(0);
    }

    [Fact]
    public async Task A_user_without_fixed_income_gets_the_portfolio_as_before()
    {
        await using var s = await Start(withFixedIncome: false, withStocks: true);

        var portfolio = await Portfolio(s.Client, "");

        portfolio.FixedIncome.ShouldBeEmpty();
        portfolio.FixedIncomeRatesThrough.ShouldBeNull();
        portfolio.Groups.ShouldHaveSingleItem().Group.ShouldBe("Stocks");
        portfolio.Assets.Single().WeightBasisPoints.ShouldBe(10_000);
    }

    // ---- Apoio ----

    private async Task<Scenario> Start(bool withFixedIncome = true, bool withStocks = false)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var source = new FakeAssetListSource { Assets = [Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A.")] };
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday), null,
            services => services.AddScoped<IAssetListSource>(_ => source));
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        await Seed(s);

        if (withFixedIncome)
        {
            s.Wedding = await Goal(s.Client, "Casamento");
            s.Trip = await Goal(s.Client, "Viagem");
            await Deposit(s.Client, s.Wedding, 479_731, new DateOnly(2026, 7, 21));
            await Deposit(s.Client, s.Wedding, 130_000, new DateOnly(2026, 7, 21));
            await Deposit(s.Client, s.Trip, 1_047_408, new DateOnly(2026, 7, 22));
            await Deposit(s.Client, s.Trip, 900_000, new DateOnly(2026, 8, 10));
            await Deposit(s.Client, null, 100_000, new DateOnly(2026, 10, 15));
        }

        if (withStocks)
        {
            var itub3 = (await s.Client.GetFromJsonAsync<List<AssetDto>>("/assets?q=ITUB3"))!.First(a => a.Symbol == "ITUB3").Id;
            var note = await s.Client.PostAsJsonAsync("/trade-notes", new
            {
                broker = "XP", date = "2026-03-02", feesCents = 0, brokerageFeeCents = 0,
                trades = new[] { new { assetId = itub3, side = "Buy", quantity = 100, unitPriceCents = 2000 } },
            });
            note.StatusCode.ShouldBe(HttpStatusCode.Created, await note.Content.ReadAsStringAsync());
        }

        return s;
    }

    private static async Task<Guid> Goal(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/savings-goals",
            new { name, description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task Deposit(HttpClient client, Guid? goal, long cents, DateOnly date)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = goal, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = cents, productKind = "Cdb",
            indexKind = "Cdi", indexPercentE4 = 1_000_000L, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ClearRates(Scenario s)
    {
        await using var scope = s.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();
    }

    // A tabela de taxas é global: cada teste a deixa como precisa; `extra` acrescenta datas à série real.
    private static async Task Seed(Scenario s, IReadOnlyList<DailyRate>? extra = null)
    {
        await ClearRates(s);
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ReferenceRates.AddRange(RealCdi2026.Rates.Concat(extra ?? [])
            .Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
        await db.SaveChangesAsync();
    }

    private static async Task<PortfolioDto> Portfolio(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/portfolio{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortfolioDto>())!;
    }
}
