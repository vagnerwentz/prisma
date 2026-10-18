using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// O saldo bruto e a evolução dos aportes (docs/renda-fixa.md, 6, R.5), contra o Postgres das migrations reais e as taxas reais do
// Banco Central de 21/07 a 15/10/2026. Os valores esperados saem de um cálculo independente (Python, Decimal, regra da B3).
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeBalancesApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record RatesDto(bool Available, DateOnly? Through, bool Stale, bool Complete, DateOnly ExpectedLast);

    private sealed record DepositBalanceDto(
        Guid DepositId, Guid? GoalId, DateOnly DepositDate, long PrincipalCents, long IndexPercentE4, long? GrossBalanceCents,
        long? GrossYieldCents, long? FactorE8, int? RatesUsed, DateOnly? RatesThrough, string? Unavailable);

    private sealed record GroupDto(
        int DepositCount, int UnavailableCount, long PrincipalCents, long GrossBalanceCents, long GrossYieldCents, DateOnly? RatesThrough);

    private sealed record GoalBalanceDto(Guid Id, string Name, long? TargetCents, GroupDto Balance, List<DepositBalanceDto> Deposits);

    private sealed record LooseDto(GroupDto Balance, List<DepositBalanceDto> Deposits);

    private sealed record BalancesDto(DateOnly AsOf, RatesDto Rates, List<GoalBalanceDto> Goals, LooseDto Unassigned, GroupDto Total);

    private sealed record PointDto(DateOnly Date, long PrincipalCents, long GrossBalanceCents, long GrossYieldCents);

    private sealed record EvolutionDto(string Scope, DateOnly From, DateOnly To, RatesDto Rates, int UnavailableDepositCount, List<PointDto> Points);

    // Segunda-feira, 12h em São Paulo. A última taxa que deveria estar gravada é a de sexta, 16/10; a série real vai até 15/10.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Casamento { get; set; }
        public Guid Viagem { get; set; }
        public Guid Empty { get; set; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // ---- Saldo por aporte, por caixa e total ----

    // Os lotes dos extratos do dono (casos A e B), com as taxas reais: cada saldo bate com o cálculo independente.
    [Fact]
    public async Task Each_deposit_balance_is_the_independent_calculation_on_the_real_rates()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, "2026-10-16");

        var deposits = balances.Goals.SelectMany(g => g.Deposits).Concat(balances.Unassigned.Deposits).ToDictionary(d => d.PrincipalCents);
        deposits[479_731].GrossBalanceCents.ShouldBe(495_051);
        deposits[479_731].FactorE8.ShouldBe(103_193_390);
        deposits[479_731].RatesUsed.ShouldBe(61);
        deposits[479_731].RatesThrough.ShouldBe(new DateOnly(2026, 10, 15));
        deposits[130_000].GrossBalanceCents.ShouldBe(134_151);
        deposits[1_047_408].GrossBalanceCents.ShouldBe(1_080_288);
        deposits[1_047_408].RatesUsed.ShouldBe(60);
        deposits[900_000].GrossBalanceCents.ShouldBe(921_953);
        deposits[900_000].RatesUsed.ShouldBe(47);
        deposits[100_000].GrossBalanceCents.ShouldBe(100_051);
        deposits.Values.ShouldAllBe(d => d.Unavailable == null && d.GrossYieldCents == d.GrossBalanceCents - d.PrincipalCents);
    }

    [Fact]
    public async Task Each_box_and_the_loose_group_and_the_total_sum_their_deposits_already_rounded()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, "2026-10-16");

        var casamento = balances.Goals.Single(g => g.Id == s.Casamento);
        casamento.Balance.ShouldBe(new GroupDto(2, 0, 609_731, 495_051 + 134_151, 629_202 - 609_731, new DateOnly(2026, 10, 15)));
        casamento.Deposits.Count.ShouldBe(2);
        var viagem = balances.Goals.Single(g => g.Id == s.Viagem);
        viagem.Balance.ShouldBe(new GroupDto(2, 0, 1_947_408, 1_080_288 + 921_953, 2_002_241 - 1_947_408, new DateOnly(2026, 10, 15)));
        balances.Unassigned.Balance.ShouldBe(new GroupDto(1, 0, 100_000, 100_051, 51, new DateOnly(2026, 10, 15)));
        balances.Total.ShouldBe(new GroupDto(
            5, 0, 609_731 + 1_947_408 + 100_000, 629_202 + 2_002_241 + 100_051,
            (629_202 + 2_002_241 + 100_051) - (609_731 + 1_947_408 + 100_000), new DateOnly(2026, 10, 15)));
    }

    // Contra os extratos do banco (diferença máxima de R$ 0,01), pelo endpoint: rendimento bruto A = 194,70 e B = 548,32.
    [Fact]
    public async Task The_group_yields_are_within_one_cent_of_the_bank_statements()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, "2026-10-16");

        Math.Abs(balances.Goals.Single(g => g.Id == s.Casamento).Balance.GrossYieldCents - 19_470).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(balances.Goals.Single(g => g.Id == s.Viagem).Balance.GrossYieldCents - 54_832).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Without_a_reference_date_the_reference_is_today()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, null);

        balances.AsOf.ShouldBe(new DateOnly(2026, 10, 19));
        balances.Total.GrossBalanceCents.ShouldBe(629_202 + 2_002_241 + 100_051);
    }

    // Aporte feito depois da data de referência ainda não existe nela: fica de fora, e o saldo é o daquele dia (cálculo independente).
    [Theory]
    [InlineData("2026-07-21", 609_731L, 0, 0L)] // só o dia do aporte: saldo = principal; a viagem ainda não tem aporte
    [InlineData("2026-07-31", 481_751L + 130_547L, 1, 1_051_266L)] // o aporte de agosto e o de outubro ainda não existem
    [InlineData("2026-08-31", 487_021L + 131_976L, 2, 1_062_766L + 906_999L)]
    [InlineData("2026-09-30", 492_293L + 133_404L, 2, 1_074_271L + 916_818L)]
    public async Task The_balance_as_of_a_past_date_ignores_deposits_made_after_it(
        string asOf, long casamentoGross, int viagemDeposits, long viagemGross)
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, asOf);

        balances.Goals.Single(g => g.Id == s.Casamento).Balance.GrossBalanceCents.ShouldBe(casamentoGross);
        var viagem = balances.Goals.Single(g => g.Id == s.Viagem).Balance;
        (viagem.DepositCount, viagem.GrossBalanceCents).ShouldBe((viagemDeposits, viagemGross));
        balances.Unassigned.Balance.DepositCount.ShouldBe(0);
        balances.Total.GrossBalanceCents.ShouldBe(casamentoGross + viagemGross);
    }

    [Fact]
    public async Task A_reference_date_in_the_future_is_refused_because_nothing_is_projected()
    {
        await using var s = await Start();

        var response = await s.Client.GetAsync("/fixed-income/balances?asOf=2026-10-20");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.GetAsync("/fixed-income/balances?asOf=2026-10-19")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_date_before_every_deposit_gives_empty_groups_and_boxes_with_no_deposit_are_still_listed()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, "2026-07-01");

        balances.Total.ShouldBe(new GroupDto(0, 0, 0, 0, 0, null));
        balances.Goals.Select(g => g.Name).ShouldBe(["Casamento", "Reserva vazia", "Viagem"]);
        balances.Goals.Single(g => g.Id == s.Empty).Balance.ShouldBe(new GroupDto(0, 0, 0, 0, 0, null));
    }

    [Fact]
    public async Task A_box_with_no_deposits_is_listed_with_zeros_and_its_target()
    {
        await using var s = await Start();

        var balances = await Balances(s.Client, "2026-10-16");

        var empty = balances.Goals.Single(g => g.Id == s.Empty);
        empty.TargetCents.ShouldBe(1_000_000);
        empty.Balance.ShouldBe(new GroupDto(0, 0, 0, 0, 0, null));
        empty.Deposits.ShouldBeEmpty();
    }

    // ---- Estado do histórico: o saldo nunca finge estar em dia ----

    [Fact]
    public async Task The_rates_status_says_until_when_the_balance_goes_and_that_it_is_stale()
    {
        await using var s = await Start();

        var rates = (await Balances(s.Client, "2026-10-16")).Rates;

        // Segunda 19/10: a taxa de sexta (16/10) deveria estar aqui e não está.
        rates.ShouldBe(new RatesDto(true, new DateOnly(2026, 10, 15), true, true, new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public async Task With_the_expected_last_rate_stored_the_history_is_current()
    {
        await using var s = await Start();
        await Seed(s, extra: [new DailyRate(new DateOnly(2026, 10, 16), 50_788)]);

        var rates = (await Balances(s.Client, null)).Rates;

        rates.ShouldBe(new RatesDto(true, new DateOnly(2026, 10, 16), false, true, new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public async Task A_missing_business_day_in_the_middle_makes_the_history_incomplete()
    {
        await using var s = await Start();
        await Seed(s, skip: [new DateOnly(2026, 9, 2)]);

        var rates = (await Balances(s.Client, "2026-10-16")).Rates;

        rates.Complete.ShouldBeFalse();
        rates.Stale.ShouldBeTrue();
    }

    // Sem nenhuma taxa: todos sem saldo, o total conta o principal e diz que está incompleto. Nunca zero no lugar de taxa.
    [Fact]
    public async Task Without_any_rate_the_deposits_have_no_balance_and_the_totals_say_so()
    {
        await using var s = await Start();
        await ClearRates(s);

        var balances = await Balances(s.Client, "2026-10-16");

        balances.Rates.ShouldBe(new RatesDto(false, null, true, true, new DateOnly(2026, 10, 16)));
        var all = balances.Goals.SelectMany(g => g.Deposits).Concat(balances.Unassigned.Deposits).ToList();
        all.ShouldAllBe(d => d.Unavailable == "RatesUnavailable" && d.GrossBalanceCents == null && d.GrossYieldCents == null && d.FactorE8 == null);
        balances.Total.UnavailableCount.ShouldBe(5);
        balances.Total.GrossYieldCents.ShouldBe(0);
        balances.Total.GrossBalanceCents.ShouldBe(balances.Total.PrincipalCents);
        balances.Total.RatesThrough.ShouldBeNull();
    }

    // O histórico começa depois da data do aporte: só esse aporte fica sem saldo, e o grupo diz quantos.
    [Fact]
    public async Task Only_the_deposits_before_the_first_stored_rate_are_unavailable()
    {
        await using var s = await Start();
        await Seed(s, skip: [new DateOnly(2026, 7, 21)]);

        var balances = await Balances(s.Client, "2026-10-16");

        var casamento = balances.Goals.Single(g => g.Id == s.Casamento);
        casamento.Deposits.ShouldAllBe(d => d.Unavailable == "RatesUnavailable");
        casamento.Balance.ShouldBe(new GroupDto(2, 2, 609_731, 609_731, 0, null));
        var viagem = balances.Goals.Single(g => g.Id == s.Viagem);
        viagem.Balance.UnavailableCount.ShouldBe(0);
        viagem.Balance.GrossBalanceCents.ShouldBe(1_080_288 + 921_953);
        balances.Total.UnavailableCount.ShouldBe(2);
    }

    // ---- Isolamento ----

    [Fact]
    public async Task One_user_never_sees_the_balances_of_another()
    {
        await using var s = await Start();
        var bruno = await s.Factory.CreateAuthenticatedClientAsync();

        var balances = await Balances(bruno, "2026-10-16");
        var evolution = await bruno.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution");

        balances.Goals.ShouldBeEmpty();
        balances.Total.ShouldBe(new GroupDto(0, 0, 0, 0, 0, null));
        evolution!.Points.ShouldAllBe(p => p.PrincipalCents == 0 && p.GrossBalanceCents == 0);
        (await bruno.GetAsync($"/fixed-income/evolution?goal={s.Casamento}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Everything_requires_a_session()
    {
        await using var s = await Start();
        var anonymous = s.Factory.CreateHttpsClient();

        (await anonymous.GetAsync("/fixed-income/balances")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/fixed-income/evolution")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Evolução ----

    [Fact]
    public async Task The_evolution_starts_on_the_first_deposit_with_the_principal_and_ends_today()
    {
        await using var s = await Start();

        var evolution = await Evolution(s.Client, "");

        evolution.Scope.ShouldBe("all");
        evolution.From.ShouldBe(new DateOnly(2026, 7, 21));
        evolution.To.ShouldBe(new DateOnly(2026, 10, 19));
        var first = evolution.Points[0];
        (first.Date, first.PrincipalCents, first.GrossBalanceCents, first.GrossYieldCents).ShouldBe((new DateOnly(2026, 7, 21), 609_731L, 609_731L, 0L));
        evolution.Points[^1].Date.ShouldBe(new DateOnly(2026, 10, 19));
        // Cada dia com taxa (61) mais o fim do período (19/10, que não tem taxa gravada).
        evolution.Points.Count.ShouldBe(62);
        evolution.Points.Select(p => p.Date).ShouldBe(evolution.Points.Select(p => p.Date).Order().Distinct());
    }

    [Fact]
    public async Task The_principal_steps_up_on_the_deposit_days_and_the_balance_never_goes_down()
    {
        await using var s = await Start();

        var points = (await Evolution(s.Client, "")).Points;

        points.Single(p => p.Date == new DateOnly(2026, 7, 21)).PrincipalCents.ShouldBe(609_731);
        points.Single(p => p.Date == new DateOnly(2026, 7, 22)).PrincipalCents.ShouldBe(609_731 + 1_047_408);
        points.Single(p => p.Date == new DateOnly(2026, 8, 10)).PrincipalCents.ShouldBe(609_731 + 1_047_408 + 900_000);
        points.Single(p => p.Date == new DateOnly(2026, 10, 15)).PrincipalCents.ShouldBe(609_731 + 1_047_408 + 900_000 + 100_000);
        points.ShouldAllBe(p => p.GrossYieldCents == p.GrossBalanceCents - p.PrincipalCents);
        // Nenhum dia o saldo cai sem um aporte novo ter entrado: ele só sobe com o principal.
        for (var i = 1; i < points.Count; i++)
            points[i].GrossBalanceCents.ShouldBeGreaterThanOrEqualTo(points[i - 1].GrossBalanceCents);
    }

    // O contrato entre os dois endpoints: o ponto de uma data é o saldo total da balances com asOf igual a ela.
    [Theory]
    [InlineData("2026-07-21")]
    [InlineData("2026-07-31")]
    [InlineData("2026-08-10")]
    [InlineData("2026-08-31")]
    [InlineData("2026-09-30")]
    [InlineData("2026-10-15")]
    [InlineData("2026-10-19")]
    public async Task Each_point_equals_the_total_of_the_balances_for_that_date(string date)
    {
        await using var s = await Start();

        var point = (await Evolution(s.Client, "")).Points.Single(p => p.Date == DateOnly.Parse(date));
        var total = (await Balances(s.Client, date)).Total;

        (point.PrincipalCents, point.GrossBalanceCents, point.GrossYieldCents)
            .ShouldBe((total.PrincipalCents, total.GrossBalanceCents, total.GrossYieldCents));
    }

    [Fact]
    public async Task The_evolution_values_match_the_independent_calculation_at_the_month_ends()
    {
        await using var s = await Start();

        var points = (await Evolution(s.Client, "?goal=" + s.Casamento)).Points;

        points.Single(p => p.Date == new DateOnly(2026, 7, 31)).GrossBalanceCents.ShouldBe(481_751 + 130_547);
        points.Single(p => p.Date == new DateOnly(2026, 8, 31)).GrossBalanceCents.ShouldBe(487_021 + 131_976);
        points.Single(p => p.Date == new DateOnly(2026, 9, 30)).GrossBalanceCents.ShouldBe(492_293 + 133_404);
    }

    [Fact]
    public async Task The_scope_is_all_a_box_or_the_loose_deposits()
    {
        await using var s = await Start();

        var casamento = await Evolution(s.Client, "?goal=" + s.Casamento);
        var loose = await Evolution(s.Client, "?goal=none");
        var empty = await Evolution(s.Client, "?goal=" + s.Empty);

        casamento.Points[^1].PrincipalCents.ShouldBe(609_731);
        casamento.Points[0].Date.ShouldBe(new DateOnly(2026, 7, 21));
        loose.Scope.ShouldBe("none");
        loose.From.ShouldBe(new DateOnly(2026, 10, 15));
        loose.Points[^1].PrincipalCents.ShouldBe(100_000);
        empty.Points.ShouldAllBe(p => p.PrincipalCents == 0 && p.GrossBalanceCents == 0);
        empty.Points.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_period_limits_the_points()
    {
        await using var s = await Start();

        var evolution = await Evolution(s.Client, "?from=2026-09-01&to=2026-09-30");

        evolution.Points[0].Date.ShouldBe(new DateOnly(2026, 9, 1));
        evolution.Points[^1].Date.ShouldBe(new DateOnly(2026, 9, 30));
        evolution.Points.ShouldAllBe(p => p.Date >= new DateOnly(2026, 9, 1) && p.Date <= new DateOnly(2026, 9, 30));
        // O principal de 01/09 já tem os aportes de julho e de agosto.
        evolution.Points[0].PrincipalCents.ShouldBe(609_731 + 1_947_408);
    }

    [Fact]
    public async Task By_month_only_the_last_point_of_each_month_stays_plus_the_first_and_the_last_of_the_period()
    {
        await using var s = await Start();

        var points = (await Evolution(s.Client, "?step=month")).Points;

        points.Select(p => p.Date).ShouldBe([
            new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30),
            new DateOnly(2026, 10, 19)]);
        points.Single(p => p.Date == new DateOnly(2026, 9, 30)).GrossBalanceCents
            .ShouldBe(492_293 + 133_404 + 1_074_271 + 916_818);
    }

    [Fact]
    public async Task By_week_there_is_at_most_one_point_per_week_and_the_ends_of_the_period_stay()
    {
        await using var s = await Start();

        var weekly = (await Evolution(s.Client, "?step=week")).Points;
        var daily = (await Evolution(s.Client, "")).Points;

        weekly.Count.ShouldBeLessThan(daily.Count / 3);
        weekly[0].Date.ShouldBe(daily[0].Date);
        weekly[^1].ShouldBe(daily[^1]);
        weekly.Select(p => (p.Date.DayNumber - new DateOnly(2026, 7, 20).DayNumber) / 7).Distinct().Count()
            .ShouldBeGreaterThanOrEqualTo(weekly.Count - 1);
        weekly.ShouldAllBe(w => daily.Contains(w));
    }

    [Theory]
    [InlineData("?step=year")]
    [InlineData("?goal=abc")]
    [InlineData("?from=2026-10-01&to=2026-09-01")]
    [InlineData("?to=2026-10-20")]
    public async Task Invalid_parameters_are_refused(string query)
    {
        await using var s = await Start();

        (await s.Client.GetAsync("/fixed-income/evolution" + query)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_user_with_nothing_gets_a_single_empty_point()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var evolution = (await client.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution"))!;

        evolution.Points.ShouldBe([new PointDto(new DateOnly(2026, 10, 19), 0, 0, 0)]);
    }

    [Fact]
    public async Task Deposits_without_a_rate_enter_the_evolution_by_the_principal_and_are_counted()
    {
        await using var s = await Start();
        await Seed(s, skip: [new DateOnly(2026, 7, 21)]);

        var evolution = await Evolution(s.Client, "?goal=" + s.Casamento);

        evolution.UnavailableDepositCount.ShouldBe(2);
        evolution.Points.ShouldAllBe(p => p.GrossBalanceCents == p.PrincipalCents && p.GrossYieldCents == 0);
    }

    // ---- Imposto e líquido estimados (R.7) ----

    // IR de 22,5 % (todos os aportes têm de 67 a 87 dias) sobre o rendimento de cada um, e o IOF de 96 % no aporte de 1 dia; valores
    // feitos à mão a partir dos saldos acima: 15.320 × 22,5 % = 3.447; 4.151 × 22,5 % = 933,975 → 934; 32.880 × 22,5 % = 7.398;
    // 21.953 × 22,5 % = 4.939,425 → 4.939; 51 × 96 % = 48,96 → 49 e o IR do que sobra (2) arredonda para 0.
    [Fact]
    public async Task Each_deposit_carries_its_own_estimated_tax_and_net_balance()
    {
        await using var s = await Start();

        var tax = (await TaxByPrincipal(s.Client, "2026-10-16"));

        tax[479_731].ShouldBe((0L, 3_447L, 2_250, 87, 495_051L - 3_447, 15_320L - 3_447));
        tax[130_000].ShouldBe((0L, 934L, 2_250, 87, 134_151L - 934, 4_151L - 934));
        tax[1_047_408].ShouldBe((0L, 7_398L, 2_250, 86, 1_080_288L - 7_398, 32_880L - 7_398));
        tax[900_000].ShouldBe((0L, 4_939L, 2_250, 67, 921_953L - 4_939, 21_953L - 4_939));
        tax[100_000].ShouldBe((49L, 0L, 2_250, 1, 100_051L - 49, 51L - 49));
    }

    [Fact]
    public async Task Boxes_and_the_total_sum_the_tax_of_each_deposit_never_one_rate_over_the_group()
    {
        await using var s = await Start();

        var groups = await TaxGroups(s.Client, "2026-10-16", s);

        groups.Casamento.ShouldBe((4_381L, 19_471L - 4_381, 629_202L - 4_381));
        groups.Viagem.ShouldBe((12_337L, 54_833L - 12_337, 2_002_241L - 12_337));
        groups.Loose.ShouldBe((49L, 2L, 100_051L - 49));
        groups.Total.ShouldBe((16_767L, 74_355L - 16_767, 2_731_494L - 16_767));
    }

    [Fact]
    public async Task The_tax_reports_the_rule_version_that_was_used()
    {
        await using var s = await Start();

        using var json = JsonDocument.Parse(await s.Client.GetStringAsync("/fixed-income/balances?asOf=2026-10-16"));

        json.RootElement.GetProperty("total").GetProperty("estimatedTaxCents").GetInt64().ShouldBe(16_767);
        var versions = json.RootElement.GetProperty("unassigned").GetProperty("deposits").EnumerateArray()
            .Select(d => d.GetProperty("tax").GetProperty("ruleVersion").GetString());
        versions.ShouldAllBe(v => v == FixedIncomeTax.CurrentRuleVersion);
    }

    // No dia do aporte não há rendimento, então não há imposto: o líquido é o principal.
    [Fact]
    public async Task On_the_deposit_day_there_is_no_yield_and_so_no_tax()
    {
        await using var s = await Start();

        var groups = await TaxGroups(s.Client, "2026-07-21", s);

        groups.Casamento.ShouldBe((0L, 0L, 609_731L));
    }

    // Sem taxa, o aporte entra pelo principal: não há rendimento para tributar, e o líquido é o principal. O imposto vem nulo.
    [Fact]
    public async Task A_deposit_without_a_rate_has_no_tax_and_its_net_is_the_principal()
    {
        await using var s = await Start();
        await ClearRates(s);

        using var json = JsonDocument.Parse(await s.Client.GetStringAsync("/fixed-income/balances?asOf=2026-10-16"));

        var total = json.RootElement.GetProperty("total");
        total.GetProperty("estimatedTaxCents").GetInt64().ShouldBe(0);
        total.GetProperty("netBalanceCents").GetInt64().ShouldBe(609_731 + 1_947_408 + 100_000);
        total.GetProperty("netYieldCents").GetInt64().ShouldBe(0);
        json.RootElement.GetProperty("unassigned").GetProperty("deposits")[0].GetProperty("tax").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- Apoio ----

    private PrismaApiFactory Factory() => new(
        postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));

    private async Task<Scenario> Start()
    {
        var factory = Factory();
        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        await ClearRates(s);

        s.Casamento = await Goal(s.Client, "Casamento");
        s.Viagem = await Goal(s.Client, "Viagem");
        s.Empty = await Goal(s.Client, "Reserva vazia", target: 1_000_000);
        await Deposit(s.Client, s.Casamento, 479_731, new DateOnly(2026, 7, 21));
        await Deposit(s.Client, s.Casamento, 130_000, new DateOnly(2026, 7, 21));
        await Deposit(s.Client, s.Viagem, 1_047_408, new DateOnly(2026, 7, 22));
        await Deposit(s.Client, s.Viagem, 900_000, new DateOnly(2026, 8, 10));
        await Deposit(s.Client, null, 100_000, new DateOnly(2026, 10, 15));

        // Os aportes são lançados sem taxa carregada; as taxas reais entram depois.
        await Seed(s);
        return s;
    }

    private static async Task<Guid> Goal(HttpClient client, string name, long? target = null)
    {
        var response = await client.PostAsJsonAsync("/savings-goals",
            new { name, description = (string?)null, targetCents = target, defaultIndexPercentE4 = (long?)null });
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task ClearRates(Scenario s)
    {
        await using var scope = s.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();
    }

    // A tabela de taxas é global: cada teste a deixa como precisa. `skip` tira datas da série real, `extra` acrescenta.
    private static async Task Seed(Scenario s, IReadOnlyCollection<DateOnly>? skip = null, IReadOnlyList<DailyRate>? extra = null)
    {
        await ClearRates(s);
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.ReferenceRates.AddRange(RealCdi2026.Rates.Concat(extra ?? [])
            .Where(r => skip is null || !skip.Contains(r.Date))
            .Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", now)));
        await db.SaveChangesAsync();
    }

    private static async Task<BalancesDto> Balances(HttpClient client, string? asOf) =>
        (await client.GetFromJsonAsync<BalancesDto>(asOf is null ? "/fixed-income/balances" : $"/fixed-income/balances?asOf={asOf}"))!;

    private static async Task<EvolutionDto> Evolution(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<EvolutionDto>("/fixed-income/evolution" + query))!;

    private static async Task<Dictionary<long, (long Iof, long Ir, int RateBp, int Days, long NetBalance, long NetYield)>> TaxByPrincipal(
        HttpClient client, string asOf)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync($"/fixed-income/balances?asOf={asOf}"));
        var root = json.RootElement;
        return root.GetProperty("goals").EnumerateArray().SelectMany(g => g.GetProperty("deposits").EnumerateArray())
            .Concat(root.GetProperty("unassigned").GetProperty("deposits").EnumerateArray())
            .ToDictionary(
                d => d.GetProperty("principalCents").GetInt64(),
                d =>
                {
                    var t = d.GetProperty("tax");
                    return (t.GetProperty("iofCents").GetInt64(), t.GetProperty("irCents").GetInt64(), t.GetProperty("irRateBasisPoints").GetInt32(),
                        t.GetProperty("holdingDays").GetInt32(), t.GetProperty("netBalanceCents").GetInt64(), t.GetProperty("netYieldCents").GetInt64());
                });
    }

    private static async Task<(
        (long Tax, long NetYield, long NetBalance) Casamento, (long Tax, long NetYield, long NetBalance) Viagem,
        (long Tax, long NetYield, long NetBalance) Loose, (long Tax, long NetYield, long NetBalance) Total)> TaxGroups(
        HttpClient client, string asOf, Scenario s)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync($"/fixed-income/balances?asOf={asOf}"));
        var root = json.RootElement;
        (long, long, long) Of(JsonElement group) =>
            (group.GetProperty("estimatedTaxCents").GetInt64(), group.GetProperty("netYieldCents").GetInt64(), group.GetProperty("netBalanceCents").GetInt64());
        JsonElement Goal(Guid id) => root.GetProperty("goals").EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id).GetProperty("balance");
        return (Of(Goal(s.Casamento)), Of(Goal(s.Viagem)), Of(root.GetProperty("unassigned").GetProperty("balance")), Of(root.GetProperty("total")));
    }
}
