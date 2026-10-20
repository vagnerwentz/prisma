using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// A renda fixa na Carteira depois dos resgates (docs/renda-fixa-resgate.md, 4; R.26): o peso e o "aportado" são o principal que SOBRA na
// data da carteira, o resgate depois do fim do ano não conta nele, o objetivo resgatado por inteiro some e a linha conta só os aportes que
// ainda têm principal. Os valores saem do cálculo independente de FixedIncomeRedemptionTests (CDI real de 21/07 a 15/10/2026): CDB a
// 100 % do CDI; resgate de R$ 3.000,00 em 15/09/2026 de um aporte de R$ 10.000,00 tira 293.987 de principal e deixa 706.013.
[Collection(ApiCollection.Name)]
public sealed class PortfolioRedemptionTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record GroupDto(string Group, long InvestedCents, int WeightBasisPoints);

    private sealed record LineDto(Guid? GoalId, string? Name, int DepositCount, long PrincipalCents, long GrossYieldCents, int WeightBasisPoints);

    private sealed record PortfolioDto(long InvestedCents, List<GroupDto> Groups, List<LineDto> FixedIncome);

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026 (as taxas vão até 15/10).
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly DepositDay = new(2025, 6, 3);
    private static readonly DateOnly RedemptionDay = new(2026, 9, 15);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Goal { get; set; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // Um aporte de 2025 (o ano passado da carteira) e as taxas de 2026. Uma taxa antiga só aceita o aporte (o rendimento existe onde há
    // taxa), então o fator do aporte até 15/09/2026 é o mesmo de quem aportou em 21/07: 1,02045302.
    private async Task<Scenario> Start()
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ReferenceRates.ExecuteDeleteAsync();
            db.ReferenceRates.Add(ReferenceRate.From(ReferenceRateSeries.Cdi, new DailyRate(new DateOnly(2024, 1, 2), 40_000), "bcb-sgs-12", DateTime.UtcNow));
            db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        var response = await s.Client.PostAsJsonAsync("/savings-goals", new { name = "Viagem", description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null });
        s.Goal = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        return s;
    }

    private static async Task<Guid> Deposit(Scenario s, long cents, bool loose = false)
    {
        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = loose ? (Guid?)null : s.Goal, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = cents, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000L, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task Redeem(Scenario s, Guid deposit, long gross)
    {
        var response = await s.Client.PostAsJsonAsync($"/fixed-income-deposits/{deposit}/redemptions", new
        {
            clientRequestId = Guid.NewGuid(), redemptionDate = RedemptionDay, grossCents = gross, netCents = (long?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<PortfolioDto> Portfolio(Scenario s, string query)
    {
        var response = await s.Client.GetAsync($"/portfolio{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortfolioDto>())!;
    }

    [Fact]
    public async Task After_a_partial_redemption_the_portfolio_weighs_only_the_principal_that_is_left()
    {
        await using var s = await Start();
        var deposit = await Deposit(s, 1_000_000);
        await Redeem(s, deposit, 300_000);

        var portfolio = await Portfolio(s, "");

        portfolio.InvestedCents.ShouldBe(706_013);
        portfolio.Groups.ShouldHaveSingleItem().ShouldBe(new GroupDto("FixedIncome", 706_013, 10_000));
        var line = portfolio.FixedIncome.ShouldHaveSingleItem();
        (line.Name, line.DepositCount, line.PrincipalCents, line.WeightBasisPoints).ShouldBe(("Viagem", 1, 706_013L, 10_000));
    }

    [Fact]
    public async Task A_redemption_after_the_end_of_the_year_does_not_count_in_that_year()
    {
        await using var s = await Start();
        var deposit = await Deposit(s, 1_000_000);
        await Redeem(s, deposit, 300_000);

        // A carteira de 2025 é a de 31/12/2025: o resgate de setembro de 2026 ainda não tinha acontecido.
        var y2025 = await Portfolio(s, "?year=2025");

        y2025.InvestedCents.ShouldBe(1_000_000);
        y2025.FixedIncome.ShouldHaveSingleItem().PrincipalCents.ShouldBe(1_000_000);
        (await Portfolio(s, "?year=2026")).InvestedCents.ShouldBe(706_013);
    }

    [Fact]
    public async Task A_goal_redeemed_in_full_leaves_the_portfolio_but_stays_in_the_years_before_it()
    {
        await using var s = await Start();
        var deposit = await Deposit(s, 1_000_000);
        await Redeem(s, deposit, 1_020_453);

        var now = await Portfolio(s, "");
        var y2025 = await Portfolio(s, "?year=2025");

        now.FixedIncome.ShouldBeEmpty();
        now.Groups.ShouldBeEmpty();
        now.InvestedCents.ShouldBe(0);
        y2025.FixedIncome.ShouldHaveSingleItem().PrincipalCents.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task The_line_counts_only_the_deposits_that_still_have_principal()
    {
        await using var s = await Start();
        var kept = await Deposit(s, 1_000_000);
        var gone = await Deposit(s, 500_000);
        // R$ 5.000,00 a 100 % do CDI até 15/09: 500.000 × 1,02045302 = 510.227.
        await Redeem(s, gone, 510_227);

        var line = (await Portfolio(s, "")).FixedIncome.ShouldHaveSingleItem();

        (line.DepositCount, line.PrincipalCents).ShouldBe((1, 1_000_000L));
        kept.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_loose_deposits_line_counts_only_the_ones_that_still_have_principal_too()
    {
        await using var s = await Start();
        await Deposit(s, 1_000_000, loose: true);
        var gone = await Deposit(s, 500_000, loose: true);
        await Redeem(s, gone, 510_227);

        var line = (await Portfolio(s, "")).FixedIncome.ShouldHaveSingleItem();

        (line.GoalId, line.DepositCount, line.PrincipalCents).ShouldBe((null, 1, 1_000_000L));
    }
}
