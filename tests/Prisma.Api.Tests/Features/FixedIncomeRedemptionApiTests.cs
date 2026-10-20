using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// O resgate de renda fixa na API (docs/renda-fixa-resgate.md, R.23), contra o Postgres das migrations reais. Os valores saem do cálculo
// independente de FixedIncomeRedemptionTests (CDI real de 21/07 a 15/10/2026): CDB a 100 % do CDI, R$ 10.000,00 em 21/07; resgate de
// R$ 3.000,00 em 15/09 (principal 293.987, rendimento 6.013, IR 1.353, líquido 298.647) e do que sobra em 16/10 (728.559).
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeRedemptionApiTests(PostgresFixture postgres)
{
    private sealed record DepositDto(Guid Id);

    private sealed record RedemptionDto(
        Guid Id, Guid DepositId, DateOnly RedemptionDate, long GrossCents, long PrincipalCents, long YieldCents, long? IofCents,
        long? IrCents, long TaxCents, long NetCents, string TaxSource, string? TaxRuleVersion);

    private sealed record PreviewDto(
        DateOnly Date, long? BalanceCents, long? RemainingPrincipalCents, RedemptionDto? Redemption, string? Problem, string? ProblemCode,
        DateOnly? LatestDate);

    private sealed record RedeemedDto(int Count, long GrossCents, long PrincipalCents, long YieldCents, long TaxCents, long NetCents);

    private sealed record DepositBalanceDto(
        Guid DepositId, long PrincipalCents, long? GrossBalanceCents, long? GrossYieldCents, RedeemedDto? Redeemed);

    private sealed record GroupDto(
        long PrincipalCents, long GrossBalanceCents, long RedeemedGrossCents, long RedeemedTaxCents, long RedeemedNetCents);

    private sealed record LooseDto(GroupDto Balance, List<DepositBalanceDto> Deposits);

    private sealed record BalancesDto(LooseDto Unassigned, GroupDto Total);

    private sealed record PointDto(DateOnly Date, long PrincipalCents, long GrossBalanceCents);

    private sealed record EvolutionDto(List<PointDto> Points);

    private sealed record UnassignedDto(int DepositCount, long PrincipalCents);

    private sealed record GoalItemDto(Guid Id, int DepositCount, long PrincipalCents);

    private sealed record GoalsDto(List<GoalItemDto> Goals, UnassignedDto Unassigned);

    private static readonly DateOnly DepositDay = new(2026, 7, 21);
    private static readonly DateOnly FirstDay = new(2026, 9, 15);
    private static readonly DateOnly LastDay = new(2026, 10, 16);

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026 (o CDI do dia 16 ainda não chegou, só até 15/10).
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private FakeClock clock = new(Monday);

    private PrismaApiFactory Factory() => new(
        postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, clock = new FakeClock(Monday));

    private static async Task SeedRates(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ReferenceRates.ExecuteDeleteAsync();
        db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateDeposit(HttpClient client, long cents = 1_000_000, string product = "Cdb", string? note = null)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = cents, productKind = product,
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!.Id;
    }

    private static Task<HttpResponseMessage> Redeem(
        HttpClient client, Guid deposit, DateOnly date, long gross, long? net = null, Guid? requestId = null) =>
        client.PostAsJsonAsync($"/fixed-income-deposits/{deposit}/redemptions", new
        {
            clientRequestId = requestId ?? Guid.NewGuid(), redemptionDate = date, grossCents = gross, netCents = net,
        });

    private static async Task<RedemptionDto> RedeemOk(HttpClient client, Guid deposit, DateOnly date, long gross, long? net = null)
    {
        var response = await Redeem(client, deposit, date, gross, net);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RedemptionDto>())!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.TryGetProperty("code", out var code)
            ? code.GetString() : null;

    private static async Task<string?> DetailOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.TryGetProperty("detail", out var detail)
            ? detail.GetString() : null;

    private static async Task<BalancesDto> Balances(HttpClient client, string asOf) =>
        (await client.GetFromJsonAsync<BalancesDto>($"/fixed-income/balances?asOf={asOf}"))!;

    private static async Task<List<RedemptionDto>> List(HttpClient client, Guid deposit) =>
        (await client.GetFromJsonAsync<List<RedemptionDto>>($"/fixed-income-deposits/{deposit}/redemptions"))!;

    // ---------- criar ----------

    [Fact]
    public async Task A_partial_redemption_is_frozen_with_the_principal_the_yield_and_the_tax()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var redemption = await RedeemOk(client, deposit, FirstDay, 300_000);

        redemption.DepositId.ShouldBe(deposit);
        redemption.RedemptionDate.ShouldBe(FirstDay);
        redemption.GrossCents.ShouldBe(300_000);
        redemption.PrincipalCents.ShouldBe(293_987);
        redemption.YieldCents.ShouldBe(6_013);
        redemption.IofCents.ShouldBe(0);
        redemption.IrCents.ShouldBe(1_353);
        redemption.TaxCents.ShouldBe(1_353);
        redemption.NetCents.ShouldBe(298_647);
        redemption.TaxSource.ShouldBe("Estimated");
        redemption.TaxRuleVersion.ShouldBe("cdb-lei-11033-2005");
        (await List(client, deposit)).Single().ShouldBe(redemption);
    }

    [Fact]
    public async Task The_net_from_the_statement_wins()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var redemption = await RedeemOk(client, deposit, FirstDay, 300_000, net: 298_700);

        redemption.TaxSource.ShouldBe("Informed");
        redemption.TaxCents.ShouldBe(1_300);
        redemption.NetCents.ShouldBe(298_700);
        redemption.IofCents.ShouldBeNull();
        redemption.TaxRuleVersion.ShouldBeNull();

        var refused = await Redeem(client, deposit, FirstDay.AddDays(1), 1_000, net: 5_000);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_same_request_twice_gives_back_the_same_redemption()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        var request = Guid.NewGuid();

        var first = await (await Redeem(client, deposit, FirstDay, 300_000, requestId: request)).Content.ReadFromJsonAsync<RedemptionDto>();
        var again = await (await Redeem(client, deposit, FirstDay, 300_000, requestId: request)).Content.ReadFromJsonAsync<RedemptionDto>();

        again!.Id.ShouldBe(first!.Id);
        (await List(client, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_redemptions_are_refused_with_the_reason_and_nothing_is_saved()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var tooMuch = await Redeem(client, deposit, FirstDay, 1_020_454);
        tooMuch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailOf(tooMuch)).ShouldBe("O valor do resgate precisa estar entre R$ 0,01 e o saldo de R$ 10.204,53.");

        (await Redeem(client, deposit, FirstDay, 0)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Redeem(client, deposit, new DateOnly(2026, 10, 20), 1_000)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Redeem(client, deposit, DepositDay.AddDays(-1), 1_000)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Hoje (19/10) o CDI de 16/10 ainda não chegou: o saldo estaria subestimado, então recusa.
        var noRates = await Redeem(client, deposit, new DateOnly(2026, 10, 19), 1_000);
        noRates.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOf(noRates)).ShouldBe("RatesUnavailable");

        (await Redeem(client, Guid.NewGuid(), FirstDay, 1_000)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await List(client, deposit)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_simultaneous_redemptions_never_sell_the_same_balance_twice()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var responses = await Task.WhenAll(
            Redeem(client, deposit, FirstDay, 700_000), Redeem(client, deposit, FirstDay, 700_000));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        (await List(client, deposit)).Single().GrossCents.ShouldBe(700_000);
    }

    [Fact]
    public async Task Many_simultaneous_pairs_never_let_both_redemptions_through()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposits = new List<Guid>();
        for (var i = 0; i < 12; i++)
            deposits.Add(await CreateDeposit(client));

        // Cada aporte recebe dois pedidos de 70 % do saldo ao mesmo tempo: dois resgates passariam do que há.
        var responses = await Task.WhenAll(deposits.SelectMany(d => new[]
        {
            Redeem(client, d, FirstDay, 700_000), Redeem(client, d, FirstDay, 700_000),
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(deposits.Count);
        foreach (var deposit in deposits)
            (await List(client, deposit)).Count.ShouldBe(1);
    }

    // ---------- prévia ----------

    private static async Task<PreviewDto> Preview(HttpClient client, Guid deposit, string query) =>
        (await client.GetFromJsonAsync<PreviewDto>($"/fixed-income-deposits/{deposit}/redemptions/preview{query}"))!;

    [Fact]
    public async Task The_preview_shows_the_redemption_as_it_would_be_saved_and_saves_nothing()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var preview = await Preview(client, deposit, "?date=2026-09-15&grossCents=300000");

        preview.BalanceCents.ShouldBe(1_020_453);
        preview.RemainingPrincipalCents.ShouldBe(1_000_000);
        preview.Problem.ShouldBeNull();
        var redemption = preview.Redemption!;
        (redemption.PrincipalCents, redemption.YieldCents, redemption.TaxCents, redemption.NetCents).ShouldBe((293_987L, 6_013L, 1_353L, 298_647L));

        // O que a prévia mostra é o que o pedido de verdade grava.
        var saved = await RedeemOk(client, deposit, FirstDay, 300_000);
        (saved.PrincipalCents, saved.YieldCents, saved.TaxCents, saved.NetCents)
            .ShouldBe((redemption.PrincipalCents, redemption.YieldCents, redemption.TaxCents, redemption.NetCents));
    }

    [Fact]
    public async Task The_preview_without_a_value_gives_only_the_balance_on_the_date()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        var preview = await Preview(client, deposit, "?date=2026-10-16");

        preview.BalanceCents.ShouldBe(728_559);
        preview.RemainingPrincipalCents.ShouldBe(706_013);
        preview.Redemption.ShouldBeNull();
        preview.Problem.ShouldBeNull();
        (await List(client, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_preview_says_why_instead_of_failing_while_the_person_types()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);

        var tooMuch = await Preview(client, deposit, "?date=2026-09-15&grossCents=2000000");
        tooMuch.BalanceCents.ShouldBe(1_020_453);
        tooMuch.Redemption.ShouldBeNull();
        tooMuch.Problem.ShouldBe("O valor do resgate precisa estar entre R$ 0,01 e o saldo de R$ 10.204,53.");

        var noRates = await Preview(client, deposit, "?date=2026-10-19");
        noRates.BalanceCents.ShouldBeNull();
        noRates.ProblemCode.ShouldBe("RatesUnavailable");

        (await Preview(client, deposit, "?date=2026-10-20")).Problem.ShouldBe("A data do resgate não pode estar no futuro.");

        var wrongNet = await Preview(client, deposit, "?date=2026-09-15&grossCents=300000&netCents=5000");
        wrongNet.Problem.ShouldBe("O líquido informado precisa estar entre R$ 2.939,87 e R$ 3.000,00.");
        (await List(client, deposit)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_says_the_latest_day_the_redemption_is_possible()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var cdi = await CreateDeposit(client);

        // Hoje é segunda 19/10 e as taxas vão até quinta 15/10: a de sexta ainda não saiu, então o último dia possível é sexta 16/10.
        (await Preview(client, cdi, "")).LatestDate.ShouldBe(new DateOnly(2026, 10, 16));
        (await Preview(client, cdi, "?date=2026-09-15")).LatestDate.ShouldBe(new DateOnly(2026, 10, 16));

        // O pré-fixado não depende da série: hoje.
        var prefixed = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = 1_000_000L, productKind = "Cdb",
            indexKind = "Prefixed", rateE4 = 140_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });
        var prefixedId = (await prefixed.Content.ReadFromJsonAsync<DepositDto>())!.Id;
        (await Preview(client, prefixedId, "")).LatestDate.ShouldBe(new DateOnly(2026, 10, 19));
    }

    [Fact]
    public async Task The_preview_of_a_fully_redeemed_deposit_says_so_and_is_private()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var stranger = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, LastDay, 1_031_934);

        (await Preview(client, deposit, "?date=2026-10-16")).Problem.ShouldBe("Esse aporte já foi resgatado por inteiro.");
        (await stranger.GetAsync($"/fixed-income-deposits/{deposit}/redemptions/preview")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- o que sobra ----------

    [Fact]
    public async Task The_balance_keeps_only_what_is_left_and_shows_what_was_redeemed()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        // Antes do resgate (14/09) nada mudou; no dia seguinte (16/09) sobra o principal 706.013 rendendo.
        var before = (await Balances(client, "2026-09-14")).Unassigned.Deposits.Single();
        before.PrincipalCents.ShouldBe(1_000_000);
        before.GrossBalanceCents.ShouldBe(1_019_926);
        before.Redeemed.ShouldBeNull();

        var after = await Balances(client, "2026-09-16");
        var item = after.Unassigned.Deposits.Single();
        item.PrincipalCents.ShouldBe(706_013);
        item.GrossBalanceCents.ShouldBe(720_825);
        item.GrossYieldCents.ShouldBe(14_812);
        item.Redeemed.ShouldBe(new RedeemedDto(1, 300_000, 293_987, 6_013, 1_353, 298_647));
        after.Total.PrincipalCents.ShouldBe(706_013);
        after.Total.GrossBalanceCents.ShouldBe(720_825);
        after.Total.RedeemedGrossCents.ShouldBe(300_000);
        after.Total.RedeemedTaxCents.ShouldBe(1_353);
        after.Total.RedeemedNetCents.ShouldBe(298_647);
    }

    [Fact]
    public async Task Redeeming_everything_leaves_a_deposit_with_no_balance_and_the_totals_keep_what_was_paid()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        var last = await RedeemOk(client, deposit, LastDay, 728_559);

        last.PrincipalCents.ShouldBe(706_013);
        last.IrCents.ShouldBe(5_073);
        last.NetCents.ShouldBe(723_486);
        var balances = await Balances(client, "2026-10-16");
        var item = balances.Unassigned.Deposits.Single();
        item.PrincipalCents.ShouldBe(0);
        item.GrossBalanceCents.ShouldBe(0);
        item.Redeemed!.Count.ShouldBe(2);
        balances.Total.PrincipalCents.ShouldBe(0);
        balances.Total.GrossBalanceCents.ShouldBe(0);
        balances.Total.RedeemedNetCents.ShouldBe(298_647 + 723_486);

        var again = await Redeem(client, deposit, LastDay, 1);
        (await DetailOf(again)).ShouldBe("Esse aporte já foi resgatado por inteiro.");
    }

    [Fact]
    public async Task The_evolution_drops_on_the_day_of_the_redemption()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        var evolution = (await client.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution?to=2026-09-16"))!;

        var points = evolution.Points.ToDictionary(p => p.Date);
        points[FirstDay.AddDays(-1)].GrossBalanceCents.ShouldBe(1_019_926);
        points[FirstDay.AddDays(-1)].PrincipalCents.ShouldBe(1_000_000);
        points[FirstDay].GrossBalanceCents.ShouldBe(720_453);
        points[FirstDay].PrincipalCents.ShouldBe(706_013);
        points[FirstDay.AddDays(1)].GrossBalanceCents.ShouldBe(720_825);
    }

    [Fact]
    public async Task A_redemption_on_a_day_with_no_rate_still_gets_its_own_point()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        var saturday = new DateOnly(2026, 9, 12);
        await RedeemOk(client, deposit, saturday, 300_000);

        var evolution = (await client.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution?to=2026-09-14"))!;

        var point = evolution.Points.Single(p => p.Date == saturday);
        point.PrincipalCents.ShouldBeLessThan(1_000_000);
        evolution.Points.Single(p => p.Date == saturday.AddDays(-1)).PrincipalCents.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task The_goals_list_counts_only_the_principal_that_is_left()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        var goals = (await client.GetFromJsonAsync<GoalsDto>("/savings-goals"))!;

        goals.Unassigned.DepositCount.ShouldBe(1);
        goals.Unassigned.PrincipalCents.ShouldBe(706_013);
    }

    [Fact]
    public async Task A_goal_counts_only_the_principal_that_is_left_of_its_own_deposits()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var goalResponse = await client.PostAsJsonAsync("/savings-goals", new { name = "Viagem", description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null });
        var goalId = (await goalResponse.Content.ReadFromJsonAsync<DepositDto>())!.Id;
        var inside = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = 1_000_000L, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
        });
        var insideId = (await inside.Content.ReadFromJsonAsync<DepositDto>())!.Id;
        var loose = await CreateDeposit(client, 500_000);
        await RedeemOk(client, insideId, FirstDay, 300_000);

        var goals = (await client.GetFromJsonAsync<GoalsDto>("/savings-goals"))!;

        goals.Goals.Single().PrincipalCents.ShouldBe(706_013);
        goals.Goals.Single().DepositCount.ShouldBe(1);
        goals.Unassigned.PrincipalCents.ShouldBe(500_000);
        loose.ShouldNotBe(Guid.Empty);
    }

    // ---------- contrato travado ----------

    [Fact]
    public async Task A_deposit_with_redemptions_keeps_its_contract_but_the_note_still_changes()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        object Update(long amount, string? note) => new
        {
            depositDate = DepositDay, amountCents = amount, productKind = "Cdb", indexKind = "Cdi", rateE4 = 1_000_000,
            maturityDate = (DateOnly?)null, liquidity = "Daily", note,
        };

        var locked = await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit}", Update(2_000_000, null));
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(locked)).ShouldBe("RedeemedDepositLocked");
        (await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit}", Update(1_000_000, "CDB do banco"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // Sem resgates, volta a poder mudar.
        var other = await CreateDeposit(client);
        (await client.PutAsJsonAsync($"/fixed-income-deposits/{other}", Update(2_000_000, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- excluir e desfazer ----------

    [Fact]
    public async Task Only_the_last_redemption_is_deleted_and_the_undo_brings_it_back()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        var first = await RedeemOk(client, deposit, FirstDay, 300_000);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var last = await RedeemOk(client, deposit, LastDay, 100_000);

        var refused = await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{first.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(refused)).ShouldBe("RedemptionNotLast");
        (await List(client, deposit)).Count.ShouldBe(2);

        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{last.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(client, deposit)).Select(r => r.Id).ShouldBe([first.Id]);
        (await Balances(client, "2026-10-16")).Total.RedeemedGrossCents.ShouldBe(300_000);

        var restored = await client.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{last.Id}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List(client, deposit)).Select(r => r.Id).ShouldBe([first.Id, last.Id]);
        (await Balances(client, "2026-10-16")).Total.RedeemedGrossCents.ShouldBe(400_000);

        // Excluir o que não existe, ou de outro aporte, dá 404.
        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_redemption_only_comes_back_when_the_deposit_is_as_it_was()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        var first = await RedeemOk(client, deposit, FirstDay, 300_000);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var second = await RedeemOk(client, deposit, LastDay, 100_000);

        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{second.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{first.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // O segundo foi congelado sobre o que o primeiro deixou: não volta antes dele.
        var early = await client.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{second.Id}/restore", null);
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOf(early)).ShouldBe("RedemptionCannotReturn");

        (await client.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{first.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{second.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List(client, deposit)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_redemption_created_after_the_deletion_blocks_the_old_one_from_returning()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        var old = await RedeemOk(client, deposit, LastDay, 300_000);
        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{old.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await RedeemOk(client, deposit, FirstDay, 200_000);

        var back = await client.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{old.Id}/restore", null);

        back.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await List(client, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_redemptions_follow_the_deposit_when_it_is_deleted_and_restored()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        (await client.DeleteAsync($"/fixed-income-deposits/{deposit}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/fixed-income-deposits/{deposit}/redemptions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Balances(client, "2026-10-16")).Total.RedeemedGrossCents.ShouldBe(0);

        (await client.PostAsync($"/fixed-income-deposits/{deposit}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List(client, deposit)).Count.ShouldBe(1);
        (await Balances(client, "2026-10-16")).Total.RedeemedGrossCents.ShouldBe(300_000);
    }

    // ---------- isolamento e banco ----------

    [Fact]
    public async Task One_user_never_sees_or_touches_the_redemptions_of_another()
    {
        await using var factory = Factory();
        var owner = await factory.CreateAuthenticatedClientAsync();
        var stranger = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(owner);
        var redemption = await RedeemOk(owner, deposit, FirstDay, 300_000);

        (await stranger.GetAsync($"/fixed-income-deposits/{deposit}/redemptions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Redeem(stranger, deposit, FirstDay, 1_000)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/fixed-income-deposits/{deposit}/redemptions/{redemption.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/fixed-income-deposits/{deposit}/redemptions/{redemption.Id}/restore", null)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Balances(stranger, "2026-10-16")).Total.RedeemedGrossCents.ShouldBe(0);
        (await List(owner, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_a_redemption_whose_numbers_do_not_add_up()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SeedRates(factory);
        var deposit = await CreateDeposit(client);
        await RedeemOk(client, deposit, FirstDay, 300_000);

        async Task<PostgresException> Corrupt(string set)
        {
            await using var connection = new NpgsqlConnection(postgres.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"UPDATE fixed_income_redemptions SET {set}", connection);
            return await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        (await Corrupt("tax_cents = yield_cents + 1, net_cents = gross_cents - yield_cents - 1")).ConstraintName
            .ShouldBe("ck_fixed_income_redemptions_tax");
        (await Corrupt("principal_cents = principal_cents + 1")).ConstraintName.ShouldBe("ck_fixed_income_redemptions_split");
        (await Corrupt("net_cents = net_cents + 1")).ConstraintName.ShouldBe("ck_fixed_income_redemptions_tax");
        (await Corrupt("tax_source = 'Informed'")).ConstraintName.ShouldBe("ck_fixed_income_redemptions_tax_detail");
        (await Corrupt("iof_cents = 1")).ConstraintName.ShouldBe("ck_fixed_income_redemptions_tax_detail");
    }
}
