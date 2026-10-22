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

// O resgate em lote (docs/renda-fixa-resgate.md, 15, R.29), contra o Postgres das migrations reais. O oráculo de cada pedaço é a prévia do
// resgate de um aporte só (já provada em FixedIncomeRedemptionApiTests): o lote tem de dar, para cada aporte, exatamente o que aquele
// resgate daria sozinho. Cenário: três aportes de CDB a 100 % do CDI, de R$ 10.000,00 (21/07), R$ 5.000,00 (23/07) e R$ 3.000,00 (10/08),
// resgatados em 15/09/2026.
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeRedemptionBatchApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record FundingDto(Guid? TransferPairId, Guid? IncomeTransactionId, Guid? InvestmentAccountId, Guid DestinationAccountId);

    private sealed record RedemptionDto(
        Guid Id, Guid DepositId, long GrossCents, long PrincipalCents, long YieldCents, long TaxCents, long NetCents, string TaxSource,
        FundingDto? Funding);

    private sealed record TotalsDto(long GrossCents, long PrincipalCents, long YieldCents, long TaxCents, long NetCents);

    private sealed record LineDto(Guid DepositId, long? BalanceCents, long? RemainingPrincipalCents, string? Skipped, RedemptionDto? Redemption);

    private sealed record PreviewDto(
        DateOnly Date, long TotalBalanceCents, List<LineDto> Lines, TotalsDto? Totals, string? Problem, string? ProblemCode);

    private sealed record BatchDto(List<RedemptionDto> Redemptions, TotalsDto Totals);

    private sealed record SinglePreviewDto(long? BalanceCents, RedemptionDto? Redemption);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents);

    private static readonly DateOnly FirstDay = new(2026, 9, 15);

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Checking { get; set; }
        public Guid Investment { get; set; }
        public Guid D1 { get; set; }
        public Guid D2 { get; set; }
        public Guid D3 { get; set; }
        public Guid[] All => [D1, D2, D3];

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // `thirdFunded: false` deixa o terceiro aporte só acompanhado (não saiu de uma conta pelo app).
    private async Task<Scenario> Start(bool thirdFunded = true)
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, new FakeClock(Monday));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ReferenceRates.ExecuteDeleteAsync();
            db.ReferenceRates.AddRange(RealCdi2026.Rates.Select(r => ReferenceRate.From(ReferenceRateSeries.Cdi, r, "bcb-sgs-12", DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        s.Checking = await Account(s.Client, new { name = "Itaú", type = "Checking", initialBalanceCents = 2_000_000 });
        s.Investment = await Account(s.Client, new { name = "Itaú Investimentos", type = "Investment", initialBalanceCents = 0 });
        s.D1 = await Deposit(s, new DateOnly(2026, 7, 21), 1_000_000, funded: true);
        s.D2 = await Deposit(s, new DateOnly(2026, 7, 23), 500_000, funded: true);
        s.D3 = await Deposit(s, new DateOnly(2026, 8, 10), 300_000, funded: thirdFunded);
        return s;
    }

    private static async Task<Guid> Account(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> Deposit(Scenario s, DateOnly date, long cents, bool funded)
    {
        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = date, amountCents = cents, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            funding = funded ? new { fromAccountId = s.Checking, toAccountId = s.Investment, method = "Pix" } : null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static string Query(IEnumerable<Guid> ids, DateOnly date, bool all = false, long? gross = null, long? net = null) =>
        string.Join("&", ids.Select(id => $"depositIds={id}")) + $"&date={date:yyyy-MM-dd}" + (all ? "&all=true" : "")
        + (gross is null ? "" : $"&grossCents={gross}") + (net is null ? "" : $"&netCents={net}");

    private static async Task<PreviewDto> Preview(
        HttpClient client, IEnumerable<Guid> ids, DateOnly date, bool all = false, long? gross = null, long? net = null)
    {
        var response = await client.GetAsync($"/fixed-income-deposits/redemptions/batch/preview?{Query(ids, date, all, gross, net)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PreviewDto>())!;
    }

    private static Task<HttpResponseMessage> Batch(
        HttpClient client, IEnumerable<Guid> ids, DateOnly date, bool all = false, long? gross = null, long? net = null, Guid? to = null,
        Guid? request = null) =>
        client.PostAsJsonAsync("/fixed-income-deposits/redemptions/batch", new
        {
            clientRequestId = request ?? Guid.NewGuid(), depositIds = ids, redemptionDate = date, all, grossCents = gross, netCents = net,
            funding = to is null ? null : new { toAccountId = to },
        });

    private static async Task<BatchDto> BatchOk(
        HttpClient client, IEnumerable<Guid> ids, DateOnly date, bool all = false, long? gross = null, long? net = null, Guid? to = null,
        Guid? request = null)
    {
        var response = await Batch(client, ids, date, all, gross, net, to, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BatchDto>())!;
    }

    // O oráculo: o que o resgate de um aporte só, com aquele valor, daria.
    private static async Task<SinglePreviewDto> Single(HttpClient client, Guid deposit, DateOnly date, long? gross = null) =>
        (await client.GetFromJsonAsync<SinglePreviewDto>(
            $"/fixed-income-deposits/{deposit}/redemptions/preview?date={date:yyyy-MM-dd}" + (gross is null ? "" : $"&grossCents={gross}")))!;

    private static async Task<List<RedemptionDto>> List(HttpClient client, Guid deposit) =>
        (await client.GetFromJsonAsync<List<RedemptionDto>>($"/fixed-income-deposits/{deposit}/redemptions"))!;

    private static async Task<long> Balance(Scenario s, Guid account) =>
        (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == account).BalanceCents!.Value;

    private static async Task<(long D1, long D2, long D3)> Balances(Scenario s) =>
        ((await Single(s.Client, s.D1, FirstDay)).BalanceCents!.Value, (await Single(s.Client, s.D2, FirstDay)).BalanceCents!.Value,
            (await Single(s.Client, s.D3, FirstDay)).BalanceCents!.Value);

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        if (code is null)
            return;

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private static void ShouldMatchSingle(RedemptionDto batch, RedemptionDto single)
    {
        batch.GrossCents.ShouldBe(single.GrossCents);
        batch.PrincipalCents.ShouldBe(single.PrincipalCents);
        batch.YieldCents.ShouldBe(single.YieldCents);
        batch.TaxCents.ShouldBe(single.TaxCents);
        batch.NetCents.ShouldBe(single.NetCents);
    }

    // ---------- prévia ----------

    [Fact]
    public async Task The_preview_takes_the_oldest_in_full_and_the_next_in_part_exactly_as_a_single_redemption_would()
    {
        await using var s = await Start();
        var (b1, _, _) = await Balances(s);
        var gross = b1 + 100_000;

        var preview = await Preview(s.Client, s.All, FirstDay, gross: gross);

        preview.Problem.ShouldBeNull();
        preview.Lines.Select(l => l.DepositId).ShouldBe(s.All);
        preview.Lines[0].Redemption!.GrossCents.ShouldBe(b1);
        preview.Lines[1].Redemption!.GrossCents.ShouldBe(100_000);
        preview.Lines[2].Redemption.ShouldBeNull();
        ShouldMatchSingle(preview.Lines[0].Redemption!, (await Single(s.Client, s.D1, FirstDay, b1)).Redemption!);
        ShouldMatchSingle(preview.Lines[1].Redemption!, (await Single(s.Client, s.D2, FirstDay, 100_000)).Redemption!);
        preview.Totals!.GrossCents.ShouldBe(gross);
        preview.Totals.TaxCents.ShouldBe(preview.Lines.Sum(l => l.Redemption?.TaxCents ?? 0));
        preview.Totals.NetCents.ShouldBe(gross - preview.Totals.TaxCents);
    }

    [Fact]
    public async Task The_preview_without_a_value_only_shows_the_balances()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);

        var preview = await Preview(s.Client, s.All, FirstDay);

        preview.Problem.ShouldBeNull();
        preview.Totals.ShouldBeNull();
        preview.TotalBalanceCents.ShouldBe(b1 + b2 + b3);
        preview.Lines.Select(l => l.BalanceCents).ShouldBe([b1, b2, b3]);
        preview.Lines.ShouldAllBe(l => l.Redemption == null);
    }

    [Fact]
    public async Task A_deposit_made_after_the_date_is_left_out()
    {
        await using var s = await Start();
        var date = new DateOnly(2026, 8, 1);

        var preview = await Preview(s.Client, s.All, date, all: true);

        preview.Lines[2].Skipped.ShouldBe("NotYetDeposited");
        preview.Lines[2].BalanceCents.ShouldBeNull();
        preview.Lines.Take(2).ShouldAllBe(l => l.Redemption != null);
    }

    [Fact]
    public async Task A_value_above_the_balances_comes_back_as_a_problem_with_the_balances_still_there()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);

        var preview = await Preview(s.Client, s.All, FirstDay, gross: b1 + b2 + b3 + 1);

        preview.Problem.ShouldNotBeNull();
        preview.Totals.ShouldBeNull();
        preview.TotalBalanceCents.ShouldBe(b1 + b2 + b3);
        preview.Lines.ShouldAllBe(l => l.BalanceCents != null);
    }

    // ---------- gravação ----------

    [Fact]
    public async Task The_batch_writes_one_redemption_per_deposit_with_the_numbers_of_a_single_redemption()
    {
        await using var s = await Start();
        var (b1, b2, _) = await Balances(s);
        var gross = b1 + 100_000;

        var batch = await BatchOk(s.Client, s.All, FirstDay, gross: gross);

        batch.Redemptions.Count.ShouldBe(2);
        batch.Redemptions[0].DepositId.ShouldBe(s.D1);
        batch.Redemptions[0].GrossCents.ShouldBe(b1);
        batch.Redemptions[1].DepositId.ShouldBe(s.D2);
        batch.Redemptions[1].GrossCents.ShouldBe(100_000);
        batch.Totals.GrossCents.ShouldBe(gross);
        batch.Totals.NetCents.ShouldBe(batch.Redemptions.Sum(r => r.NetCents));

        (await List(s.Client, s.D1)).Single().GrossCents.ShouldBe(b1);
        (await List(s.Client, s.D2)).Single().GrossCents.ShouldBe(100_000);
        (await List(s.Client, s.D3)).ShouldBeEmpty();
        (await Single(s.Client, s.D2, FirstDay)).BalanceCents!.Value.ShouldBeLessThan(b2);
    }

    [Fact]
    public async Task Redeeming_everything_empties_every_deposit()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);

        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true);

        batch.Redemptions.Select(r => r.GrossCents).ShouldBe([b1, b2, b3]);
        foreach (var deposit in s.All)
            (await Single(s.Client, deposit, FirstDay)).BalanceCents.ShouldBe(0);
    }

    [Fact]
    public async Task The_dropped_deposit_gets_nothing_and_the_value_moves_on_to_the_next()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);

        // O primeiro aporte foi desmarcado na prévia: o valor sai do segundo e do terceiro.
        var batch = await BatchOk(s.Client, [s.D2, s.D3], FirstDay, gross: b2 + 50_000);

        batch.Redemptions.Select(r => (r.DepositId, r.GrossCents)).ShouldBe([(s.D2, b2), (s.D3, 50_000)]);
        (await List(s.Client, s.D1)).ShouldBeEmpty();
        b1.ShouldBeGreaterThan(0);
        b3.ShouldBeGreaterThan(50_000);
    }

    [Fact]
    public async Task Money_lands_in_one_account_and_an_unfunded_deposit_is_only_a_record()
    {
        await using var s = await Start(thirdFunded: false);
        var (b1, b2, b3) = await Balances(s);
        var checkingBefore = await Balance(s, s.Checking);
        var investmentBefore = await Balance(s, s.Investment);

        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking);

        batch.Redemptions[0].Funding!.DestinationAccountId.ShouldBe(s.Checking);
        batch.Redemptions[1].Funding!.DestinationAccountId.ShouldBe(s.Checking);
        batch.Redemptions[2].Funding.ShouldBeNull();

        // Só os dois aportes que saíram de uma conta devolvem dinheiro: o principal (transferência) e o rendimento líquido (receita).
        var funded = batch.Redemptions.Take(2).ToList();
        (await Balance(s, s.Checking)).ShouldBe(checkingBefore + funded.Sum(r => r.NetCents));
        (await Balance(s, s.Investment)).ShouldBe(investmentBefore - funded.Sum(r => r.PrincipalCents));
        b1.ShouldBeGreaterThan(0);
        (b2 + b3).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_statement_net_is_split_between_the_deposits_and_adds_up()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);
        var estimated = await BatchPreview(s, b1 + b2 + b3);
        // O banco cobrou 3 centavos a menos de imposto do que o app estimou.
        var statementNet = estimated.Totals!.NetCents + 3;

        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true, net: statementNet);

        batch.Totals.NetCents.ShouldBe(statementNet);
        batch.Redemptions.Sum(r => r.NetCents).ShouldBe(statementNet);
        batch.Redemptions.ShouldAllBe(r => r.TaxSource == "Informed");
    }

    private static async Task<PreviewDto> BatchPreview(Scenario s, long gross) => await Preview(s.Client, s.All, FirstDay, gross: gross);

    // ---------- tudo ou nada ----------

    [Fact]
    public async Task One_deposit_that_cannot_be_redeemed_stops_the_whole_batch()
    {
        await using var s = await Start();
        // O segundo aporte já teve um resgate em 20/09: um lote em 15/09 ficaria antes dele.
        var early = await s.Client.PostAsJsonAsync($"/fixed-income-deposits/{s.D2}/redemptions", new
        {
            clientRequestId = Guid.NewGuid(), redemptionDate = new DateOnly(2026, 9, 20), grossCents = 10_000L,
        });
        early.StatusCode.ShouldBe(HttpStatusCode.OK, await early.Content.ReadAsStringAsync());

        var response = await Batch(s.Client, s.All, FirstDay, all: true);

        await ShouldBeProblem(response, HttpStatusCode.BadRequest);
        (await List(s.Client, s.D1)).ShouldBeEmpty();
        (await List(s.Client, s.D3)).ShouldBeEmpty();
        (await List(s.Client, s.D2)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_destination_account_writes_nothing()
    {
        await using var s = await Start();

        await ShouldBeProblem(await Batch(s.Client, s.All, FirstDay, all: true, to: Guid.NewGuid()), HttpStatusCode.NotFound);

        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deposit_that_is_not_yours_is_not_found_and_nothing_is_written()
    {
        await using var s = await Start();
        var stranger = await s.Factory.CreateAuthenticatedClientAsync();

        await ShouldBeProblem(await Batch(stranger, s.All, FirstDay, all: true), HttpStatusCode.NotFound);
        await ShouldBeProblem(await Batch(s.Client, [s.D1, Guid.NewGuid()], FirstDay, all: true), HttpStatusCode.NotFound);

        (await stranger.GetAsync($"/fixed-income-deposits/redemptions/batch/preview?{Query(s.All, FirstDay)}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).ShouldBeEmpty();
    }

    // ---------- pedido repetido ----------

    [Fact]
    public async Task Repeating_the_same_request_returns_the_same_redemptions_and_creates_no_more()
    {
        await using var s = await Start();
        var request = Guid.NewGuid();
        var first = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking, request: request);
        var checking = await Balance(s, s.Checking);

        var again = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking, request: request);

        again.Redemptions.Select(r => r.Id).Order().ShouldBe(first.Redemptions.Select(r => r.Id).Order());
        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).Count.ShouldBe(1);
        (await Balance(s, s.Checking)).ShouldBe(checking);
    }

    [Fact]
    public async Task Two_identical_requests_at_once_create_one_set()
    {
        await using var s = await Start();
        var request = Guid.NewGuid();

        var responses = await Task.WhenAll(
            Batch(s.Client, s.All, FirstDay, all: true, request: request), Batch(s.Client, s.All, FirstDay, all: true, request: request));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThan(0);
        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_different_batches_at_once_never_sell_the_same_balance_twice()
    {
        await using var s = await Start();

        // Pedidos diferentes (não é toque duplo) sobre os mesmos aportes: os dois leriam o mesmo saldo. O token de concorrência do aporte
        // deixa passar um só; o outro leva 409 (ou 400, se chegou depois e já não havia saldo).
        var responses = await Task.WhenAll(
            Batch(s.Client, s.All, FirstDay, all: true), Batch(s.Client, s.All, FirstDay, all: true));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).Count.ShouldBe(1);
    }

    // ---------- validação ----------

    [Fact]
    public async Task The_request_needs_a_value_or_everything_but_not_both()
    {
        await using var s = await Start();

        await ShouldBeProblem(await Batch(s.Client, s.All, FirstDay), HttpStatusCode.BadRequest);
        await ShouldBeProblem(await Batch(s.Client, s.All, FirstDay, all: true, gross: 1_000), HttpStatusCode.BadRequest);
        await ShouldBeProblem(await Batch(s.Client, [], FirstDay, all: true), HttpStatusCode.BadRequest);
        await ShouldBeProblem(await Batch(s.Client, [s.D1, s.D1], FirstDay, all: true), HttpStatusCode.BadRequest);
        await ShouldBeProblem(await Batch(s.Client, s.All, FirstDay, gross: 0), HttpStatusCode.BadRequest);
        await ShouldBeProblem(await Batch(s.Client, s.All, new DateOnly(2026, 10, 20), all: true), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_value_above_the_balances_is_refused_and_writes_nothing()
    {
        await using var s = await Start();
        var (b1, b2, b3) = await Balances(s);

        await ShouldBeProblem(await Batch(s.Client, s.All, FirstDay, gross: b1 + b2 + b3 + 1), HttpStatusCode.BadRequest);

        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).ShouldBeEmpty();
    }

    // ---------- desfazer o lote ----------

    private static Task<HttpResponseMessage> Undo(HttpClient client, BatchDto batch, string? transfer = null) =>
        client.PostAsJsonAsync("/fixed-income-deposits/redemptions/batch/undo", new
        {
            items = batch.Redemptions.Select(r => new { depositId = r.DepositId, redemptionId = r.Id }),
            transfer,
        });

    [Fact]
    public async Task Undoing_the_batch_removes_every_redemption_and_the_entries_in_the_accounts()
    {
        await using var s = await Start();
        var checkingBefore = await Balance(s, s.Checking);
        var investmentBefore = await Balance(s, s.Investment);
        var balancesBefore = await Balances(s);
        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking);

        var response = await Undo(s.Client, batch, "delete");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(checkingBefore);
        (await Balance(s, s.Investment)).ShouldBe(investmentBefore);
        (await Balances(s)).ShouldBe(balancesBefore);
    }

    [Fact]
    public async Task Undoing_with_entries_in_the_accounts_asks_whether_they_stay()
    {
        await using var s = await Start();
        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking);

        await ShouldBeProblem(await Undo(s.Client, batch), HttpStatusCode.BadRequest, "TransferChoiceRequired");

        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Undoing_keeps_the_entries_when_asked_to()
    {
        await using var s = await Start();
        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true, to: s.Checking);
        var checking = await Balance(s, s.Checking);

        (await Undo(s.Client, batch, "keep")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(checking);
    }

    [Fact]
    public async Task A_batch_that_is_no_longer_the_last_redemption_of_a_deposit_cannot_be_undone_and_nothing_changes()
    {
        await using var s = await Start();
        var (b1, _, _) = await Balances(s);
        var batch = await BatchOk(s.Client, s.All, FirstDay, gross: b1 / 2);
        // Um resgate depois do lote no mesmo aporte: o do lote deixa de ser o último.
        var later = await s.Client.PostAsJsonAsync($"/fixed-income-deposits/{s.D1}/redemptions", new
        {
            clientRequestId = Guid.NewGuid(), redemptionDate = new DateOnly(2026, 9, 20), grossCents = 10_000L,
        });
        later.StatusCode.ShouldBe(HttpStatusCode.OK, await later.Content.ReadAsStringAsync());

        await ShouldBeProblem(await Undo(s.Client, batch), HttpStatusCode.Conflict, "RedemptionNotLast");

        (await List(s.Client, s.D1)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_stranger_cannot_undo_my_batch()
    {
        await using var s = await Start();
        var stranger = await s.Factory.CreateAuthenticatedClientAsync();
        var batch = await BatchOk(s.Client, s.All, FirstDay, all: true);

        await ShouldBeProblem(await Undo(stranger, batch), HttpStatusCode.NotFound);

        foreach (var deposit in s.All)
            (await List(s.Client, deposit)).Count.ShouldBe(1);
    }
}
