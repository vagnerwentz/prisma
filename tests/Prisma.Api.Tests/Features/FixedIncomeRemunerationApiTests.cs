using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// CDI+ e pré-fixado na API (docs/renda-fixa.md, 5.5, R.11): a taxa do aporte é uma só (`rateE4`) e o índice diz o que ela é; o
// nome antigo (`indexPercentE4`) segue valendo no CDI para a tela aberta antes desta versão. Os valores esperados saem de um cálculo
// independente (Python, Decimal), com as taxas reais do Banco Central de 21/07 a 15/10/2026.
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeRemunerationApiTests(PostgresFixture postgres)
{
    private sealed record DepositDto(
        Guid Id, DateOnly DepositDate, long AmountCents, string IndexKind, long RateE4, long? IndexPercentE4);

    private sealed record TaxDto(long TotalCents);

    private sealed record DepositBalanceDto(
        Guid DepositId, DateOnly DepositDate, long PrincipalCents, string IndexKind, long RateE4, long? IndexPercentE4,
        long? GrossBalanceCents, long? FactorE8, int? RatesUsed, DateOnly? RatesThrough, string? Unavailable, TaxDto? Tax);

    private sealed record RatesDto(bool Available, DateOnly? Through);

    private sealed record GroupDto(int DepositCount, int UnavailableCount, long PrincipalCents, long GrossBalanceCents, DateOnly? RatesThrough);

    private sealed record LooseDto(GroupDto Balance, List<DepositBalanceDto> Deposits);

    private sealed record BalancesDto(DateOnly AsOf, RatesDto Rates, LooseDto Unassigned, GroupDto Total);

    private sealed record PointDto(DateOnly Date, long PrincipalCents, long GrossBalanceCents);

    private sealed record EvolutionDto(int UnavailableDepositCount, List<PointDto> Points);

    private sealed record LineDto(int DepositCount, long PrincipalCents, long GrossYieldCents);

    private sealed record PortfolioDto(List<LineDto> FixedIncome, DateOnly? FixedIncomeRatesThrough);

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private PrismaApiFactory Factory() => new(
        postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));

    private static async Task SeedRates(PrismaApiFactory factory, bool any)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ReferenceRates.ExecuteDeleteAsync();
        if (!any)
            return;

        db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
        await db.SaveChangesAsync();
    }

    private static object Body(string index, DateOnly date, long amount, long? rateE4 = null, long? legacy = null) => new
    {
        goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = amount, productKind = "Cdb",
        indexKind = index, rateE4, indexPercentE4 = legacy, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
    };

    private static async Task<DepositDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!;
    }

    private static async Task<BalancesDto> Balances(HttpClient client, string asOf) =>
        (await client.GetFromJsonAsync<BalancesDto>($"/fixed-income/balances?asOf={asOf}"))!;

    // ---- CDI+ ----

    // R$ 6.097,31 a CDI + 2 % a.a. de 21/07 a 16/10/2026: 61 taxas usadas, fator 1,03689235, saldo R$ 6.322,25.
    [Fact]
    public async Task A_cdi_plus_deposit_matches_the_independent_calculation()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        var created = await Create(client, Body("CdiPlus", new DateOnly(2026, 7, 21), 609_731, rateE4: 20_000));

        created.ShouldBe(new DepositDto(created.Id, new DateOnly(2026, 7, 21), 609_731, "CdiPlus", 20_000, null));
        var item = (await Balances(client, "2026-10-16")).Unassigned.Deposits.Single();
        item.IndexKind.ShouldBe("CdiPlus");
        item.RateE4.ShouldBe(20_000);
        item.GrossBalanceCents.ShouldBe(632_225);
        item.FactorE8.ShouldBe(103_689_235);
        item.RatesUsed.ShouldBe(61);
        item.RatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
        item.Tax.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_cdi_plus_deposit_before_the_first_rate_is_refused_like_the_cdi()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", Body("CdiPlus", new DateOnly(2026, 7, 20), 100_000, rateE4: 20_000));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("BeforeFirstRate");
    }

    // ---- Pré-fixado ----

    // R$ 5.000 a 14 % a.a. de 01/10 a 16/10/2026: 10 dias úteis (12/10 é feriado), fator 1,00521308, saldo R$ 5.026,07.
    [Fact]
    public async Task A_prefixed_deposit_matches_the_independent_calculation_and_ignores_holidays()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        await Create(client, Body("Prefixed", new DateOnly(2026, 10, 1), 500_000, rateE4: 140_000));

        var item = (await Balances(client, "2026-10-16")).Unassigned.Deposits.Single();
        item.IndexKind.ShouldBe("Prefixed");
        item.RateE4.ShouldBe(140_000);
        item.IndexPercentE4.ShouldBeNull();
        item.GrossBalanceCents.ShouldBe(502_607);
        item.FactorE8.ShouldBe(100_521_308);
        item.RatesUsed.ShouldBe(10);
        item.RatesThrough.ShouldBeNull();
        item.Tax.ShouldNotBeNull();
    }

    // A data do aporte só é conferida contra o histórico do CDI quando o aporte usa o CDI: o pré-fixado de janeiro entra com o histórico
    // começando em 21/07, e o CDI e o CDI+ da mesma data não.
    [Fact]
    public async Task Only_the_deposits_that_use_the_cdi_are_checked_against_the_start_of_its_history()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        var january = new DateOnly(2026, 1, 5);

        var prefixed = await client.PostAsJsonAsync("/fixed-income-deposits", Body("Prefixed", january, 100_000, rateE4: 140_000));
        var cdi = await client.PostAsJsonAsync("/fixed-income-deposits", Body("Cdi", january, 100_000, rateE4: 1_000_000));
        var cdiPlus = await client.PostAsJsonAsync("/fixed-income-deposits", Body("CdiPlus", january, 100_000, rateE4: 20_000));

        prefixed.StatusCode.ShouldBe(HttpStatusCode.OK);
        cdi.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        cdiPlus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // O mesmo na edição: mover a data do aporte para antes do histórico do CDI só é recusado se ele usa o CDI.
    [Fact]
    public async Task Editing_the_date_before_the_cdi_history_is_refused_only_for_the_deposits_that_use_the_cdi()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        var prefixed = await Create(client, Body("Prefixed", new DateOnly(2026, 10, 1), 100_000, rateE4: 140_000));
        var cdiPlus = await Create(client, Body("CdiPlus", new DateOnly(2026, 10, 1), 100_000, rateE4: 20_000));

        Task<HttpResponseMessage> MoveToJanuary(DepositDto deposit, string index, long rate) =>
            client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", new
            {
                depositDate = new DateOnly(2026, 1, 5), amountCents = 100_000L, productKind = "Cdb", indexKind = index, rateE4 = rate,
                maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            });

        (await MoveToJanuary(prefixed, "Prefixed", 140_000)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MoveToJanuary(cdiPlus, "CdiPlus", 20_000)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // O pré-fixado vale até hoje mesmo com a série do CDI mais atrasada que isso, e até sem nenhuma taxa gravada (Banco Central fora do ar).
    [Fact]
    public async Task A_prefixed_balance_does_not_need_the_cdi_history()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: false);

        // Antes do primeiro dia do histórico (que nem existe) e de qualquer taxa: o pré-fixado não olha o CDI.
        await Create(client, Body("Prefixed", new DateOnly(2026, 1, 5), 300_000, rateE4: 120_000));

        var balances = await Balances(client, "2026-10-19");
        var item = balances.Unassigned.Deposits.Single();
        balances.Rates.Available.ShouldBeFalse();
        item.Unavailable.ShouldBeNull();
        item.RatesUsed.ShouldBe(197);
        item.GrossBalanceCents.ShouldBe(327_791);
        balances.Total.UnavailableCount.ShouldBe(0);
        balances.Total.GrossBalanceCents.ShouldBe(327_791);
    }

    [Fact]
    public async Task A_cdi_deposit_without_rates_still_comes_out_unavailable_beside_a_prefixed_one()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: false);
        await Create(client, Body("Cdi", new DateOnly(2026, 10, 6), 100_000, rateE4: 1_000_000));
        await Create(client, Body("Prefixed", new DateOnly(2026, 10, 6), 250_000, rateE4: 100_000));

        var balances = await Balances(client, "2026-10-19");

        balances.Total.UnavailableCount.ShouldBe(1);
        balances.Unassigned.Deposits.Single(d => d.IndexKind == "Cdi").Unavailable.ShouldBe("RatesUnavailable");
        balances.Unassigned.Deposits.Single(d => d.IndexKind == "Prefixed").GrossBalanceCents.ShouldBe(250_758);
        // O grupo vale pelo principal do aporte sem taxa e pelo saldo do pré.
        balances.Total.GrossBalanceCents.ShouldBe(100_000 + 250_758);
    }

    // A data da última taxa do grupo vem só dos aportes que usam o CDI: o pré-fixado não puxa a data para trás nem para a frente.
    [Fact]
    public async Task The_group_rates_through_comes_only_from_the_deposits_that_use_the_cdi()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        await Create(client, Body("Cdi", new DateOnly(2026, 8, 10), 100_000, rateE4: 1_000_000));
        await Create(client, Body("Prefixed", new DateOnly(2026, 10, 1), 500_000, rateE4: 140_000));

        var balances = await Balances(client, "2026-10-19");

        balances.Total.RatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
        balances.Total.UnavailableCount.ShouldBe(0);
    }

    // O ponto de uma data da evolução é o saldo do mesmo aporte na balances com asOf igual a ela.
    [Fact]
    public async Task The_evolution_includes_cdi_plus_and_prefixed_with_the_same_numbers_as_the_balance()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        await Create(client, Body("CdiPlus", new DateOnly(2026, 7, 21), 609_731, rateE4: 20_000));
        await Create(client, Body("Prefixed", new DateOnly(2026, 10, 1), 500_000, rateE4: 140_000));

        var evolution = (await client.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution?goal=all&to=2026-10-16&step=day"))!;

        evolution.UnavailableDepositCount.ShouldBe(0);
        var last = evolution.Points[^1];
        last.Date.ShouldBe(new DateOnly(2026, 10, 16));
        last.PrincipalCents.ShouldBe(609_731 + 500_000);
        last.GrossBalanceCents.ShouldBe(632_225 + 502_607);
        last.GrossBalanceCents.ShouldBe((await Balances(client, "2026-10-16")).Total.GrossBalanceCents);
    }

    // ---- Carteira ----

    // A Carteira soma o aportado e o rendimento de qualquer índice numa linha só, e a data das taxas é a do CDI: sem aporte que use o CDI,
    // não há data (o pré-fixado não tem taxa do Banco Central para mostrar).
    [Fact]
    public async Task The_portfolio_counts_cdi_plus_and_prefixed_and_has_no_rates_date_when_none_uses_the_cdi()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        await Create(client, Body("Prefixed", new DateOnly(2026, 10, 6), 250_000, rateE4: 100_000));

        var onlyPrefixed = (await client.GetFromJsonAsync<PortfolioDto>("/portfolio"))!;
        await Create(client, Body("CdiPlus", new DateOnly(2026, 7, 21), 609_731, rateE4: 20_000));
        var both = (await client.GetFromJsonAsync<PortfolioDto>("/portfolio"))!;

        var line = onlyPrefixed.FixedIncome.Single();
        (line.DepositCount, line.PrincipalCents, line.GrossYieldCents).ShouldBe((1, 250_000L, 758L));
        onlyPrefixed.FixedIncomeRatesThrough.ShouldBeNull();
        both.FixedIncome.Single().DepositCount.ShouldBe(2);
        both.FixedIncome.Single().PrincipalCents.ShouldBe(250_000 + 609_731);
        both.FixedIncomeRatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
    }

    // ---- Validação ----

    [Theory]
    [InlineData("Prefixed", 1_100_000)] // 110 %: serve de percentual do CDI, não de taxa ao ano
    [InlineData("Prefixed", 99)]
    [InlineData("CdiPlus", 0)]
    [InlineData("CdiPlus", 1_000_001)]
    [InlineData("Cdi", 5_000_001)]
    public async Task A_rate_outside_the_range_of_its_index_is_refused(string index, long rate)
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", Body(index, new DateOnly(2026, 10, 1), 100_000, rateE4: rate));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_deposit_with_no_rate_is_refused_and_the_old_name_only_counts_for_the_cdi()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        var none = await client.PostAsJsonAsync("/fixed-income-deposits", Body("Cdi", new DateOnly(2026, 10, 1), 100_000));
        var oldNameOnPrefixed = await client.PostAsJsonAsync(
            "/fixed-income-deposits", Body("Prefixed", new DateOnly(2026, 10, 1), 100_000, legacy: 140_000));

        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).ShouldContain("Informe a taxa");
        oldNameOnPrefixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Compatibilidade com a tela aberta antes da versão ----

    // A tela antiga manda e lê `indexPercentE4`: no CDI, o deposit sai igual, com o nome novo e o antigo.
    [Fact]
    public async Task The_old_field_name_still_works_for_the_cdi_and_both_names_come_back()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);

        var old = await Create(client, Body("Cdi", new DateOnly(2026, 8, 10), 100_000, legacy: 1_100_000));
        var current = await Create(client, Body("Cdi", new DateOnly(2026, 8, 10), 100_000, rateE4: 1_050_000));

        (old.RateE4, old.IndexPercentE4).ShouldBe((1_100_000L, (long?)1_100_000L));
        (current.RateE4, current.IndexPercentE4).ShouldBe((1_050_000L, (long?)1_050_000L));
        var listed = (await client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits?goal=none"))!;
        listed.Select(d => d.RateE4).Order().ShouldBe([1_050_000L, 1_100_000L]);
    }

    [Fact]
    public async Task An_edit_can_change_the_kind_and_the_rate_and_the_balance_follows()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        var created = await Create(client, Body("Cdi", new DateOnly(2026, 10, 1), 500_000, rateE4: 1_000_000));

        var response = await client.PutAsJsonAsync($"/fixed-income-deposits/{created.Id}", new
        {
            depositDate = new DateOnly(2026, 10, 1), amountCents = 500_000L, productKind = "Cdb", indexKind = "Prefixed", rateE4 = 140_000L,
            maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = (await response.Content.ReadFromJsonAsync<DepositDto>())!;
        (edited.IndexKind, edited.RateE4, edited.IndexPercentE4).ShouldBe(("Prefixed", 140_000L, (long?)null));
        (await Balances(client, "2026-10-16")).Unassigned.Deposits.Single().GrossBalanceCents.ShouldBe(502_607);
    }

    [Fact]
    public async Task An_edit_with_a_rate_that_does_not_fit_the_new_kind_is_refused_and_nothing_changes()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        var created = await Create(client, Body("Cdi", new DateOnly(2026, 10, 1), 500_000, rateE4: 1_100_000));

        // 110 % do CDI virou "pré a 110 % ao ano": não cabe numa taxa ao ano (máximo de 100 %).
        var response = await client.PutAsJsonAsync($"/fixed-income-deposits/{created.Id}", new
        {
            depositDate = new DateOnly(2026, 10, 1), amountCents = 500_000L, productKind = "Cdb", indexKind = "Prefixed", rateE4 = 1_100_000L,
            maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var listed = (await client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits?goal=none"))!.Single();
        (listed.IndexKind, listed.RateE4).ShouldBe(("Cdi", 1_100_000L));
    }

    // ---- O banco também garante ----

    [Theory]
    [InlineData("Prefixed", 1_100_000)]
    [InlineData("CdiPlus", 50)]
    [InlineData("Cdi", 5_000_001)]
    public async Task The_database_refuses_a_rate_outside_the_range_of_its_index(string index, long rate)
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory, any: true);
        var created = await Create(client, Body(index, new DateOnly(2026, 10, 1), 100_000, rateE4: index == "Cdi" ? 1_000_000 : 100_000));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var update = () => db.Database.ExecuteSqlAsync($"UPDATE fixed_income_deposits SET rate_e4 = {rate} WHERE id = {created.Id}");

        var thrown = await Should.ThrowAsync<PostgresException>(update);
        thrown.ConstraintName.ShouldBe("ck_fixed_income_deposits_rate");
    }
}
