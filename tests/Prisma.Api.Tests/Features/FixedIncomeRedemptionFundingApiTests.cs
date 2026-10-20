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

// O resgate que cai numa conta (docs/renda-fixa-resgate.md, 5, R.24), contra o Postgres das migrations reais: o principal volta como
// transferência (da conta de investimento para a de destino), o rendimento líquido entra como receita, e o imposto não é despesa. Os
// valores são os do resgate de R$ 3.000,00 em 15/09 de FixedIncomeRedemptionApiTests (principal 293.987, IR 1.353, líquido 298.647).
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeRedemptionFundingApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record FundingDto(Guid? TransferPairId, Guid? IncomeTransactionId, Guid? InvestmentAccountId, Guid DestinationAccountId);

    private sealed record RedemptionDto(Guid Id, long PrincipalCents, long NetCents, FundingDto? Funding);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents);

    private sealed record SummaryDto(long IncomeCents, long ExpenseCents, long LeftoverCents, long InvestedCents);

    private sealed record EntryDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly PurchaseDate, Guid? TransferPairId, string? TransferDirection,
        Guid? CategoryId, string? Description);

    private static readonly DateOnly DepositDay = new(2026, 7, 21);
    private static readonly DateOnly FirstDay = new(2026, 9, 15);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Checking { get; set; }
        public Guid Cash { get; set; }
        public Guid Investment { get; set; }
        public Guid Card { get; set; }
        public Guid Deposit { get; set; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // Segunda-feira, 12h em São Paulo: "hoje" é 19/10/2026.
    private static readonly DateTime Monday = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    // Um aporte de R$ 10.000,00 que saiu do Itaú (corrente, R$ 20.000,00) para a conta de investimento, em 21/07.
    private async Task<Scenario> Start(bool funded = true)
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
        s.Cash = await Account(s.Client, new { name = "Carteira", type = "Cash", initialBalanceCents = 300_000 });
        s.Investment = await Account(s.Client, new { name = "Itaú Investimentos", type = "Investment", initialBalanceCents = 0 });
        s.Card = await Account(s.Client, new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = 1_000_000L, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            funding = funded ? new { fromAccountId = s.Checking, toAccountId = s.Investment, method = "Pix" } : null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        s.Deposit = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        return s;
    }

    private static async Task<Guid> Account(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static Task<HttpResponseMessage> Redeem(
        Scenario s, Guid? to, long gross = 300_000, long? net = null, Guid? request = null, Guid? deposit = null, HttpClient? client = null) =>
        (client ?? s.Client).PostAsJsonAsync($"/fixed-income-deposits/{deposit ?? s.Deposit}/redemptions", new
        {
            clientRequestId = request ?? Guid.NewGuid(), redemptionDate = FirstDay, grossCents = gross, netCents = net,
            funding = to is null ? null : new { toAccountId = to },
        });

    private static async Task<RedemptionDto> RedeemOk(Scenario s, Guid? to, long gross = 300_000, long? net = null, Guid? request = null)
    {
        var response = await Redeem(s, to, gross, net, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RedemptionDto>())!;
    }

    private static async Task<long> Balance(Scenario s, Guid account) =>
        (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == account).BalanceCents!.Value;

    private static async Task<SummaryDto> Summary(HttpClient client, string month) =>
        (await client.GetFromJsonAsync<SummaryDto>($"/dashboard/summary?month={month}"))!;

    // Só o que o resgate lançou: o que cai em 15/09 (o aporte é de 21/07).
    private static async Task<List<EntryDto>> Entries(HttpClient client) =>
        (await client.GetFromJsonAsync<List<EntryDto>>("/transactions?from=2026-09-01&to=2026-09-30"))!;

    private static async Task<List<RedemptionDto>> List(Scenario s) =>
        (await s.Client.GetFromJsonAsync<List<RedemptionDto>>($"/fixed-income-deposits/{s.Deposit}/redemptions"))!;

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    // ---------- o que o resgate lança ----------

    [Fact]
    public async Task The_principal_comes_back_as_a_transfer_and_the_net_yield_enters_as_income()
    {
        await using var s = await Start();
        var before = await Summary(s.Client, "2026-09");

        var redemption = await RedeemOk(s, s.Checking);

        var entries = await Entries(s.Client);
        entries.Count.ShouldBe(3);
        var transfer = entries.Where(e => e.TransferPairId is not null).ToList();
        transfer.ShouldAllBe(e => e.Type == "Transfer" && e.AmountCents == 293_987 && e.PurchaseDate == FirstDay);
        transfer.Single(e => e.TransferDirection == "Out").AccountId.ShouldBe(s.Investment);
        transfer.Single(e => e.TransferDirection == "In").AccountId.ShouldBe(s.Checking);
        var income = entries.Single(e => e.Type == "Income");
        (income.AmountCents, income.AccountId, income.PurchaseDate).ShouldBe((4_660L, s.Checking, FirstDay));
        income.CategoryId.ShouldNotBeNull();
        redemption.Funding.ShouldBe(new FundingDto(transfer[0].TransferPairId, income.Id, s.Investment, s.Checking));

        // O que cai na conta é o líquido (principal + rendimento líquido), e a conta de investimento fica com o que sobra aportado.
        (await Balance(s, s.Checking)).ShouldBe(2_000_000 - 1_000_000 + 298_647);
        (await Balance(s, s.Investment)).ShouldBe(1_000_000 - 293_987);
        (await Balance(s, s.Cash)).ShouldBe(300_000);
        var after = await Summary(s.Client, "2026-09");
        (after.IncomeCents - before.IncomeCents).ShouldBe(4_660);
        (after.ExpenseCents - before.ExpenseCents).ShouldBe(0);
        (after.InvestedCents - before.InvestedCents).ShouldBe(-293_987);
    }

    [Fact]
    public async Task The_wallet_can_receive_the_money_too()
    {
        await using var s = await Start();

        await RedeemOk(s, s.Cash);

        (await Balance(s, s.Cash)).ShouldBe(300_000 + 298_647);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task The_net_from_the_statement_sets_the_income()
    {
        await using var s = await Start();

        var redemption = await RedeemOk(s, s.Checking, net: 298_700);

        (await Entries(s.Client)).Single(e => e.Type == "Income").AmountCents.ShouldBe(4_713);
        redemption.NetCents.ShouldBe(298_700);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000 + 298_700);
    }

    [Fact]
    public async Task With_no_net_yield_there_is_no_income()
    {
        await using var s = await Start();

        var redemption = await RedeemOk(s, s.Checking, net: 293_987);

        (await Entries(s.Client)).Count(e => e.Type == "Income").ShouldBe(0);
        redemption.Funding!.IncomeTransactionId.ShouldBeNull();
        redemption.Funding.TransferPairId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Without_an_account_the_redemption_is_only_a_record()
    {
        await using var s = await Start();

        var redemption = await RedeemOk(s, null);

        redemption.Funding.ShouldBeNull();
        (await Entries(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Balance(s, s.Investment)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Repeating_the_same_request_launches_only_once()
    {
        await using var s = await Start();
        var request = Guid.NewGuid();

        var first = await RedeemOk(s, s.Checking, request: request);
        var again = await RedeemOk(s, s.Checking, request: request);

        again.Id.ShouldBe(first.Id);
        (await Entries(s.Client)).Count.ShouldBe(3);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000 + 298_647);
    }

    // ---------- tudo ou nada ----------

    [Fact]
    public async Task A_deposit_that_never_left_an_account_cannot_return_money_to_one()
    {
        await using var s = await Start(funded: false);

        var response = await Redeem(s, s.Checking);

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "RedemptionNeedsFundedDeposit");
        (await List(s)).ShouldBeEmpty();
        (await Entries(s.Client)).ShouldBeEmpty();
        // Sem a conta, o mesmo resgate vale como registro.
        (await Redeem(s, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_destination_must_be_a_checking_account_or_the_wallet_and_a_refusal_saves_nothing()
    {
        await using var s = await Start();

        await ShouldBeProblem(await Redeem(s, s.Investment), HttpStatusCode.BadRequest, "RedemptionNeedsPaymentAccount");
        await ShouldBeProblem(await Redeem(s, s.Card), HttpStatusCode.BadRequest, "RedemptionNeedsPaymentAccount");
        (await Redeem(s, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await List(s)).ShouldBeEmpty();
        (await Entries(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task A_refused_redemption_creates_no_entries()
    {
        await using var s = await Start();

        var tooMuch = await Redeem(s, s.Checking, gross: 5_000_000);

        tooMuch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Entries(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Nobody_can_send_the_money_to_an_account_of_someone_else()
    {
        await using var s = await Start();
        using var stranger = await s.Factory.CreateAuthenticatedClientAsync();
        var theirChecking = await Account(stranger, new { name = "Deles", type = "Checking", initialBalanceCents = 0 });
        var theirInvestment = await Account(stranger, new { name = "Deles Inv", type = "Investment", initialBalanceCents = 0 });
        var theirs = await stranger.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = DepositDay, amountCents = 1_000_000L, productKind = "Cdb",
            indexKind = "Cdi", rateE4 = 1_000_000, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            funding = new { fromAccountId = theirChecking, toAccountId = theirInvestment, method = "Pix" },
        });
        var theirDeposit = (await theirs.Content.ReadFromJsonAsync<IdDto>())!.Id;

        // O estranho aponta para a conta do dono: o filtro de dono a esconde (404), e nada é lançado.
        var response = await Redeem(s, s.Checking, deposit: theirDeposit, client: stranger);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Entries(s.Client)).ShouldBeEmpty();
    }

    // ---------- excluir e desfazer ----------

    [Fact]
    public async Task Deleting_a_redemption_that_fell_in_an_account_asks_what_to_do_with_the_entries()
    {
        await using var s = await Start();
        var redemption = await RedeemOk(s, s.Checking);

        var asked = await s.Client.DeleteAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}");
        await ShouldBeProblem(asked, HttpStatusCode.BadRequest, "TransferChoiceRequired");
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}?transfer=maybe")).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        (await List(s)).Count.ShouldBe(1);
        (await Entries(s.Client)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Deleting_with_the_entries_takes_them_along_and_the_undo_brings_them_all_back()
    {
        await using var s = await Start();
        var redemption = await RedeemOk(s, s.Checking);

        (await s.Client.DeleteAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}?transfer=delete")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await Entries(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Balance(s, s.Investment)).ShouldBe(1_000_000);
        (await List(s)).ShouldBeEmpty();

        var restored = await s.Client.PostAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await restored.Content.ReadFromJsonAsync<RedemptionDto>())!.Funding.ShouldNotBeNull();
        (await Entries(s.Client)).Count.ShouldBe(3);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000 + 298_647);
        (await Balance(s, s.Investment)).ShouldBe(1_000_000 - 293_987);
    }

    [Fact]
    public async Task Keeping_the_entries_leaves_the_accounts_as_they_are()
    {
        await using var s = await Start();
        var redemption = await RedeemOk(s, s.Checking);

        (await s.Client.DeleteAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}?transfer=keep")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await List(s)).ShouldBeEmpty();
        (await Entries(s.Client)).Count.ShouldBe(3);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000 + 298_647);

        // O desfazer devolve só o resgate: os lançamentos nunca saíram, e não voltam duas vezes.
        (await s.Client.PostAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}/restore", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await Entries(s.Client)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Entries_that_were_deleted_on_their_own_are_not_shown_and_need_no_choice()
    {
        await using var s = await Start();
        var redemption = await RedeemOk(s, s.Checking);
        // Excluir uma ponta da transferência leva a outra junto; a receita sai à parte.
        var entries = await Entries(s.Client);
        foreach (var entry in new[] { entries.Single(e => e.Type == "Income"), entries.First(e => e.TransferPairId is not null) })
            (await s.Client.DeleteAsync($"/transactions/{entry.Id}")).IsSuccessStatusCode.ShouldBeTrue();
        (await Entries(s.Client)).ShouldBeEmpty();

        (await List(s)).Single().Funding.ShouldBeNull();
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{s.Deposit}/redemptions/{redemption.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_list_shows_the_funding_only_of_the_redemptions_that_have_it()
    {
        await using var s = await Start();
        var first = await RedeemOk(s, s.Checking, gross: 300_000);
        var second = await Redeem(s, null, gross: 100_000);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await List(s);

        list.Count.ShouldBe(2);
        list.Single(r => r.Id == first.Id).Funding.ShouldBe(first.Funding);
        list.Single(r => r.Id != first.Id).Funding.ShouldBeNull();
    }
}
