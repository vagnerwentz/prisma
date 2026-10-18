using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// O aporte que sai de uma conta (docs/renda-fixa.md, D3, R.4b): o app cria a transferência junto, o saldo da conta cai, o
// "Investido" do Resumo sobe e nada vira receita nem despesa. Contra o Postgres das migrations reais.
[Collection(ApiCollection.Name)]
public sealed class FundedDepositApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record FundingDto(Guid TransferPairId, Guid FromAccountId, Guid InvestmentAccountId);

    private sealed record DepositDto(
        Guid Id, Guid? GoalId, DateOnly DepositDate, long AmountCents, long IndexPercentE4, string? Note, FundingDto? Funding);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents);

    private sealed record SummaryDto(long IncomeCents, long ExpenseCents, long LeftoverCents, long InvestedCents);

    private sealed record LegDto(Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly PurchaseDate, Guid? TransferPairId, string? TransferDirection);

    private sealed record DeletedGoalDto(Guid Id, List<Guid> DetachedDepositIds, List<Guid> DeletedDepositIds);

    private static readonly DateOnly Day = new(2026, 7, 21);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }

        public PrismaApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public Guid Checking { get; set; }
        public Guid Cash { get; set; }
        public Guid Investment { get; set; }
        public Guid Card { get; set; }
    }

    // ---- A história do dono: R$ 10.000 saíram do Itaú para o investimento ----

    [Fact]
    public async Task The_money_leaves_the_account_and_the_invested_card_rises_without_touching_income_or_expense()
    {
        await using var s = await Start();
        var before = await Summary(s.Client);

        var deposit = await Fund(s, 1_000_000);

        deposit.Funding.ShouldNotBeNull();
        (deposit.Funding.FromAccountId, deposit.Funding.InvestmentAccountId).ShouldBe((s.Checking, s.Investment));
        (await Balance(s, s.Checking)).ShouldBe(2_000_000 - 1_000_000);
        (await Balance(s, s.Investment)).ShouldBe(1_000_000);
        var after = await Summary(s.Client);
        after.InvestedCents.ShouldBe(before.InvestedCents + 1_000_000);
        (after.IncomeCents, after.ExpenseCents, after.LeftoverCents).ShouldBe((before.IncomeCents, before.ExpenseCents, before.LeftoverCents));
    }

    [Fact]
    public async Task The_two_legs_are_a_transfer_pair_on_the_deposit_date_in_the_deposit_amount()
    {
        await using var s = await Start();

        var deposit = await Fund(s, 1_000_000, method: "Ted");

        var legs = await Legs(s.Client);
        legs.Count.ShouldBe(2);
        legs.ShouldAllBe(l => l.Type == "Transfer" && l.AmountCents == 1_000_000 && l.PurchaseDate == Day);
        legs.Select(l => l.TransferPairId).Distinct().ShouldBe([deposit.Funding!.TransferPairId]);
        legs.Single(l => l.TransferDirection == "Out").AccountId.ShouldBe(s.Checking);
        legs.Single(l => l.TransferDirection == "In").AccountId.ShouldBe(s.Investment);
    }

    [Fact]
    public async Task The_wallet_can_fund_a_deposit_too()
    {
        await using var s = await Start();

        var deposit = await Fund(s, 50_000, from: s.Cash);

        deposit.Funding!.FromAccountId.ShouldBe(s.Cash);
        (await Balance(s, s.Cash)).ShouldBe(300_000 - 50_000);
    }

    // Sem "saiu de qual conta" o aporte só é acompanhado: nenhum saldo mexe, nada entra no Investido.
    [Fact]
    public async Task A_deposit_without_funding_touches_no_balance_and_no_invested()
    {
        await using var s = await Start();
        var before = await Summary(s.Client);

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, funding: false));
        var deposit = (await response.Content.ReadFromJsonAsync<DepositDto>())!;

        deposit.Funding.ShouldBeNull();
        (await Legs(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(2_000_000);
        (await Summary(s.Client)).ShouldBe(before);
    }

    [Fact]
    public async Task The_list_shows_the_funding_only_for_deposits_that_have_it()
    {
        await using var s = await Start();
        var funded = await Fund(s, 100_000);
        var loose = (await (await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 5_000, funding: false)))
            .Content.ReadFromJsonAsync<DepositDto>())!;

        var list = (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!;

        list.Single(d => d.Id == funded.Id).Funding.ShouldBe(funded.Funding);
        list.Single(d => d.Id == loose.Id).Funding.ShouldBeNull();
    }

    // ---- Tudo ou nada ----

    [Fact]
    public async Task Repeating_the_same_request_funds_only_once()
    {
        await using var s = await Start();
        var request = Guid.NewGuid();

        var first = await Fund(s, 1_000_000, request: request);
        var second = await Fund(s, 1_000_000, request: request);

        second.Id.ShouldBe(first.Id);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Summary(s.Client)).InvestedCents.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Several_equal_requests_at_the_same_time_fund_only_once()
    {
        await using var s = await Start();
        var request = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, request: request))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("toChecking", "FundingNeedsInvestmentAccount")]
    [InlineData("toCard", "FundingNeedsInvestmentAccount")]
    [InlineData("fromCard", "FundingNeedsPaymentAccount")]
    [InlineData("fromInvestment", "FundingNeedsPaymentAccount")]
    public async Task A_funding_with_the_wrong_kind_of_account_creates_neither_the_deposit_nor_the_transfer(string mistake, string code)
    {
        await using var s = await Start();
        var (from, to) = mistake switch
        {
            "toChecking" => (s.Cash, s.Checking),
            "toCard" => (s.Checking, s.Card),
            "fromCard" => (s.Card, s.Investment),
            _ => (s.Investment, s.Investment),
        };

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, from: from, to: to));

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, code);
        await ShouldHaveNothing(s);
    }

    [Fact]
    public async Task An_invalid_deposit_with_a_valid_funding_creates_no_transfer()
    {
        await using var s = await Start();

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, percent: 0));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHaveNothing(s);
    }

    [Fact]
    public async Task A_funding_account_that_does_not_exist_is_not_found_and_creates_nothing()
    {
        await using var s = await Start();

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, from: Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldHaveNothing(s);
    }

    [Fact]
    public async Task Another_users_account_cannot_fund_a_deposit()
    {
        await using var alice = await Start();
        await using var bruno = await Start();

        var response = await bruno.Client.PostAsJsonAsync("/fixed-income-deposits", Body(bruno, 1_000_000, from: alice.Checking, to: alice.Investment));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Balance(alice, alice.Checking)).ShouldBe(2_000_000);
        await ShouldHaveNothing(bruno);
        await ShouldHaveNothing(alice);
    }

    [Fact]
    public async Task A_funding_without_account_ids_is_a_validation_error()
    {
        await using var s = await Start();

        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, 1_000_000, from: Guid.Empty));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHaveNothing(s);
    }

    // ---- Editar: transferência não se edita ----

    [Fact]
    public async Task The_amount_and_the_date_of_a_funded_deposit_are_locked_but_the_rest_can_change()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);

        var amount = await s.Client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", Update(amount: 900_000));
        var date = await s.Client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", Update(date: Day.AddDays(1)));
        var rest = await s.Client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", Update(percent: 1_100_000, note: "CDB 110%"));

        await ShouldBeProblem(amount, HttpStatusCode.Conflict, "FundedDepositLocked");
        await ShouldBeProblem(date, HttpStatusCode.Conflict, "FundedDepositLocked");
        rest.EnsureSuccessStatusCode();
        var edited = (await rest.Content.ReadFromJsonAsync<DepositDto>())!;
        (edited.AmountCents, edited.DepositDate, edited.IndexPercentE4, edited.Note).ShouldBe((1_000_000L, Day, 1_100_000L, "CDB 110%"));
        edited.Funding.ShouldBe(deposit.Funding);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Moving_a_funded_deposit_between_goals_keeps_the_transfer()
    {
        await using var s = await Start();
        var goal = (await (await s.Client.PostAsJsonAsync("/savings-goals",
            new { name = "Casamento", description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null }))
            .Content.ReadFromJsonAsync<IdDto>())!;
        var deposit = await Fund(s, 1_000_000);

        var moved = await s.Client.PatchAsJsonAsync($"/fixed-income-deposits/{deposit.Id}/goal", new { goalId = goal.Id });

        moved.EnsureSuccessStatusCode();
        (await moved.Content.ReadFromJsonAsync<DepositDto>())!.Funding.ShouldBe(deposit.Funding);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    // ---- Excluir: o que fazer com a transferência ----

    [Fact]
    public async Task Deleting_a_funded_deposit_requires_a_choice_and_changes_nothing_without_it()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);

        var response = await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}");

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "TransferChoiceRequired");
        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.Count.ShouldBe(1);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=talvez")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_the_deposit_and_keeping_the_transfer_leaves_the_money_moved()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);

        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=keep")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.ShouldBeEmpty();
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Summary(s.Client)).InvestedCents.ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Deleting_the_deposit_and_the_transfer_gives_the_money_back_to_the_account()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);

        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=delete")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.ShouldBeEmpty();
        (await Legs(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(2_000_000);
        (await Balance(s, s.Investment)).ShouldBe(0);
        (await Summary(s.Client)).InvestedCents.ShouldBe(0);
    }

    [Fact]
    public async Task Undoing_brings_the_deposit_and_its_transfer_back_together()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=delete")).EnsureSuccessStatusCode();

        var restored = await s.Client.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null);

        restored.EnsureSuccessStatusCode();
        (await restored.Content.ReadFromJsonAsync<DepositDto>())!.Funding.ShouldBe(deposit.Funding);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
        (await Summary(s.Client)).InvestedCents.ShouldBe(1_000_000);
    }

    // Se a transferência ficou, desfazer traz só o aporte: nunca uma segunda transferência.
    [Fact]
    public async Task Undoing_a_delete_that_kept_the_transfer_does_not_duplicate_it()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=keep")).EnsureSuccessStatusCode();

        var restored = await s.Client.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null);

        (await restored.Content.ReadFromJsonAsync<DepositDto>())!.Funding.ShouldBe(deposit.Funding);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    [Fact]
    public async Task Undoing_is_refused_when_an_account_was_deleted_meanwhile_and_nothing_comes_back()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        (await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}?transfer=delete")).EnsureSuccessStatusCode();
        (await s.Client.DeleteAsync($"/accounts/{s.Investment}")).EnsureSuccessStatusCode();

        var restored = await s.Client.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null);

        restored.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.ShouldBeEmpty();
        (await Legs(s.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_goal_with_its_investments_leaves_their_transfers_where_they_are()
    {
        await using var s = await Start();
        var goal = (await (await s.Client.PostAsJsonAsync("/savings-goals",
            new { name = "Casamento", description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null }))
            .Content.ReadFromJsonAsync<IdDto>())!;
        var deposit = await Fund(s, 1_000_000, goal: goal.Id);

        var response = await s.Client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=delete");

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<DeletedGoalDto>())!.DeletedDepositIds.ShouldBe([deposit.Id]);
        (await Legs(s.Client)).Count.ShouldBe(2);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    // ---- A tela de lançamentos mexe na transferência por conta própria ----

    [Fact]
    public async Task Deleting_the_transfer_from_the_transactions_screen_unlinks_the_funding_and_restoring_it_links_again()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        var leg = (await Legs(s.Client)).First();

        (await s.Client.DeleteAsync($"/transactions/{leg.Id}")).EnsureSuccessStatusCode();
        var whileDeleted = await ListDeposit(s, deposit.Id);
        (await s.Client.PostAsync($"/transactions/{leg.Id}/restore", null)).EnsureSuccessStatusCode();
        var afterRestore = await ListDeposit(s, deposit.Id);

        whileDeleted.Funding.ShouldBeNull();
        afterRestore.Funding.ShouldBe(deposit.Funding);
        (await Balance(s, s.Checking)).ShouldBe(1_000_000);
    }

    // Enquanto as pontas podem voltar (30 min), valor e data seguem travados; sem a transferência viva, excluir não pergunta nada.
    [Fact]
    public async Task While_the_transfer_is_deleted_the_deposit_stays_locked_and_deleting_it_asks_nothing()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        var leg = (await Legs(s.Client)).First();
        (await s.Client.DeleteAsync($"/transactions/{leg.Id}")).EnsureSuccessStatusCode();

        var edit = await s.Client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", Update(amount: 800_000));
        var delete = await s.Client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}");

        await ShouldBeProblem(edit, HttpStatusCode.Conflict, "FundedDepositLocked");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // As pontas apagadas de vez (a limpeza dos 30 minutos): o aporte volta a ser só acompanhado e pode ser editado.
    [Fact]
    public async Task When_the_transfer_is_gone_for_good_the_deposit_becomes_plain_and_editable()
    {
        await using var s = await Start();
        var deposit = await Fund(s, 1_000_000);
        await HardDeleteLegs(s);

        var edit = await s.Client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", Update(amount: 800_000));

        edit.EnsureSuccessStatusCode();
        var edited = (await edit.Content.ReadFromJsonAsync<DepositDto>())!;
        (edited.AmountCents, edited.Funding).ShouldBe((800_000L, null));
        (await PairIdInDatabase(s, deposit.Id)).ShouldBeNull();
    }

    // ---- Apoio ----

    private async Task<Scenario> Start()
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" });
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();

        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync());
        s.Checking = await Account(s.Client, new { name = "Itaú", type = "Checking", initialBalanceCents = 2_000_000 });
        s.Cash = await Account(s.Client, new { name = "Carteira", type = "Cash", initialBalanceCents = 300_000 });
        s.Investment = await Account(s.Client, new { name = "Itaú Investimentos", type = "Investment", initialBalanceCents = 0 });
        s.Card = await Account(s.Client, new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
        return s;
    }

    private static async Task<Guid> Account(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/accounts", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static object Body(
        Scenario s, long amount, bool funding = true, Guid? from = null, Guid? to = null, string method = "Pix", long percent = 1_000_000,
        Guid? request = null, Guid? goal = null) =>
        new
        {
            goalId = goal, clientRequestId = request ?? Guid.NewGuid(), depositDate = Day, amountCents = amount, productKind = "Cdb",
            indexKind = "Cdi", indexPercentE4 = percent, maturityDate = (DateOnly?)null, liquidity = "Unknown", note = (string?)null,
            funding = funding ? new { fromAccountId = from ?? s.Checking, toAccountId = to ?? s.Investment, method } : null,
        };

    private static object Update(long amount = 1_000_000, DateOnly? date = null, long percent = 1_000_000, string? note = null) =>
        new
        {
            depositDate = date ?? Day, amountCents = amount, productKind = "Cdb", indexKind = "Cdi", indexPercentE4 = percent,
            maturityDate = (DateOnly?)null, liquidity = "Unknown", note,
        };

    private static async Task<DepositDto> Fund(
        Scenario s, long amount, Guid? from = null, string method = "Pix", Guid? request = null, Guid? goal = null)
    {
        var response = await s.Client.PostAsJsonAsync("/fixed-income-deposits", Body(s, amount, from: from, method: method, request: request, goal: goal));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!;
    }

    private static async Task<DepositDto> ListDeposit(Scenario s, Guid id) =>
        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.Single(d => d.Id == id);

    private static async Task<long> Balance(Scenario s, Guid account) =>
        (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.Single(b => b.AccountId == account).BalanceCents!.Value;

    private static async Task<SummaryDto> Summary(HttpClient client) =>
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-07"))!;

    private static async Task<List<LegDto>> Legs(HttpClient client) =>
        (await client.GetFromJsonAsync<List<LegDto>>("/transactions?from=2026-01-01&to=2026-12-31"))!
            .Where(t => t.TransferPairId is not null).ToList();

    private static async Task ShouldHaveNothing(Scenario s)
    {
        (await s.Client.GetFromJsonAsync<List<DepositDto>>("/fixed-income-deposits"))!.ShouldBeEmpty();
        (await Legs(s.Client)).ShouldBeEmpty();
        (await Balance(s, s.Checking)).ShouldBe(2_000_000);
        (await Summary(s.Client)).InvestedCents.ShouldBe(0);
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task HardDeleteLegs(Scenario s)
    {
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE transactions SET refunded_transaction_id = NULL WHERE transfer_pair_id IS NOT NULL");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM transactions WHERE transfer_pair_id IS NOT NULL");
    }

    private static async Task<Guid?> PairIdInDatabase(Scenario s, Guid depositId)
    {
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<Guid?>($"SELECT transfer_pair_id AS \"Value\" FROM fixed_income_deposits WHERE id = {depositId}")
            .SingleAsync();
    }
}
