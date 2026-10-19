using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.FixedIncome;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Objetivos (as caixas) e aportes em renda fixa, contra o Postgres das migrations reais (docs/renda-fixa.md, 2.2 e 2.3, R.4).
[Collection(ApiCollection.Name)]
public sealed class FixedIncomeApiTests(PostgresFixture postgres)
{
    private sealed record GoalDto(Guid Id, string Name, string? Description, long? TargetCents, long? DefaultIndexPercentE4, int DepositCount, long PrincipalCents);

    private sealed record UnassignedDto(int DepositCount, long PrincipalCents);

    private sealed record GoalsDto(List<GoalDto> Goals, UnassignedDto Unassigned);

    private sealed record DepositDto(
        Guid Id, Guid? GoalId, DateOnly DepositDate, long AmountCents, string ProductKind, string IndexKind, long IndexPercentE4,
        DateOnly? MaturityDate, string Liquidity, string? Note);

    private sealed record DeletedGoalDto(Guid Id, List<Guid> DetachedDepositIds, List<Guid> DeletedDepositIds);

    private static readonly DateOnly Day = new(2026, 7, 21);

    private readonly LogSink _logs = new();

    // ---- Objetivos ----

    [Fact]
    public async Task A_goal_is_created_and_listed_with_zero_deposits()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await CreateGoal(client, "Casamento", "Festa e lua de mel", 5_000_000, 1_000_000);

        created.ShouldBe(new GoalDto(created.Id, "Casamento", "Festa e lua de mel", 5_000_000, 1_000_000, 0, 0));
        var all = await Goals(client);
        all.Goals.ShouldBe([created]);
        all.Unassigned.ShouldBe(new UnassignedDto(0, 0));
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("Casamento", 0L, null)]
    [InlineData("Casamento", -5L, null)]
    [InlineData("Casamento", null, 99L)]
    [InlineData("Casamento", null, 5_000_001L)]
    public async Task An_invalid_goal_is_refused_and_nothing_is_created(string name, long? target, long? percent)
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/savings-goals",
            new { name, description = (string?)null, targetCents = target, defaultIndexPercentE4 = percent });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Goals(client)).Goals.ShouldBeEmpty();
    }

    // O mesmo nome não repete, sem diferenciar maiúsculas; excluído, o nome fica livre.
    [Fact]
    public async Task A_goal_name_is_unique_ignoring_case_until_the_other_is_deleted()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var first = await CreateGoal(client, "Casamento");

        var duplicate = await client.PostAsJsonAsync("/savings-goals", GoalBody("casamento"));

        await ShouldBeProblem(duplicate, HttpStatusCode.Conflict, "GoalNameTaken");
        (await client.DeleteAsync($"/savings-goals/{first.Id}")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/savings-goals", GoalBody("casamento"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_goal_is_edited_and_cannot_take_the_name_of_another()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento", target: 1_000_000);
        await CreateGoal(client, "Viagem");

        var edited = await client.PutAsJsonAsync($"/savings-goals/{goal.Id}",
            new { name = "Lua de mel", description = "Itália", targetCents = 2_000_000L, defaultIndexPercentE4 = 1_050_000L });
        var taken = await client.PutAsJsonAsync($"/savings-goals/{goal.Id}", GoalBody("viagem"));
        var missing = await client.PutAsJsonAsync($"/savings-goals/{Guid.NewGuid()}", GoalBody("Qualquer"));

        edited.EnsureSuccessStatusCode();
        (await edited.Content.ReadFromJsonAsync<GoalDto>())!.ShouldBe(new GoalDto(goal.Id, "Lua de mel", "Itália", 2_000_000, 1_050_000, 0, 0));
        await ShouldBeProblem(taken, HttpStatusCode.Conflict, "GoalNameTaken");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Goals(client)).Goals.Select(g => g.Name).ShouldBe(["Lua de mel", "Viagem"]);
    }

    [Fact]
    public async Task Goals_are_listed_by_name_with_the_count_and_the_principal_of_their_deposits()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var casamento = await CreateGoal(client, "Casamento");
        var viagem = await CreateGoal(client, "Viagem");
        await CreateDeposit(client, casamento.Id, 400_000);
        await CreateDeposit(client, casamento.Id, 100_000);
        await CreateDeposit(client, viagem.Id, 70_000);
        await CreateDeposit(client, null, 5_000);
        await CreateDeposit(client, null, 6_000);

        var all = await Goals(client);

        all.Goals.Select(g => (g.Name, g.DepositCount, g.PrincipalCents)).ShouldBe([("Casamento", 2, 500_000L), ("Viagem", 1, 70_000L)]);
        all.Unassigned.ShouldBe(new UnassignedDto(2, 11_000));
    }

    // ---- Aportes ----

    [Fact]
    public async Task A_deposit_is_created_with_what_was_typed_and_listed()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(
            goalId: null, amountCents: 609_731, percent: 1_050_000, maturity: new DateOnly(2028, 7, 21), liquidity: "Daily", note: "  CDB do Itaú  "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deposit = (await response.Content.ReadFromJsonAsync<DepositDto>())!;
        deposit.ShouldBe(new DepositDto(deposit.Id, null, Day, 609_731, "Cdb", "Cdi", 1_050_000, new DateOnly(2028, 7, 21), "Daily", "CDB do Itaú"));
        (await Deposits(client)).ShouldBe([deposit]);
    }

    [Fact]
    public async Task Deposits_are_listed_newest_first_and_filtered_by_goal_or_loose()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var older = await CreateDeposit(client, goal.Id, 100_000, date: new DateOnly(2026, 7, 21));
        var newer = await CreateDeposit(client, goal.Id, 200_000, date: new DateOnly(2026, 8, 5));
        var loose = await CreateDeposit(client, null, 300_000, date: new DateOnly(2026, 7, 22));

        (await Deposits(client)).Select(d => d.Id).ShouldBe([newer.Id, loose.Id, older.Id]);
        (await Deposits(client, goal.Id.ToString())).Select(d => d.Id).ShouldBe([newer.Id, older.Id]);
        (await Deposits(client, "none")).Select(d => d.Id).ShouldBe([loose.Id]);
        (await client.GetAsync("/fixed-income-deposits?goal=abc")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Toque duplo, rede que repete: o mesmo clientRequestId devolve o aporte que já existe.
    [Fact]
    public async Task Sending_the_same_request_twice_returns_the_same_deposit_and_creates_one()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var request = Guid.NewGuid();

        var first = await CreateDeposit(client, null, 100_000, request: request);
        var second = await CreateDeposit(client, null, 100_000, request: request);

        second.Id.ShouldBe(first.Id);
        (await Deposits(client)).Count.ShouldBe(1);
        _logs.Entries.Count(e => e.EventId.Name == "DepositAdded").ShouldBe(1);
    }

    // O identificador é o que identifica o pedido, não o corpo: reenviar com o mesmo identificador devolve o original, sem criar
    // outro e sem mudar nada, mesmo que o reenvio traga valores diferentes (ou inválidos).
    [Fact]
    public async Task The_request_id_decides_not_the_body_of_the_resend()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var request = Guid.NewGuid();
        var original = await CreateDeposit(client, null, 100_000, request: request);

        var different = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 999_999, percent: 1_100_000, request: request));
        var invalid = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 5, date: new DateOnly(2099, 1, 1), request: request));

        different.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await different.Content.ReadFromJsonAsync<DepositDto>())!.ShouldBe(original);
        invalid.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await invalid.Content.ReadFromJsonAsync<DepositDto>())!.ShouldBe(original);
        (await Deposits(client)).ShouldBe([original]);
    }

    [Fact]
    public async Task Sending_the_same_request_at_the_same_time_still_creates_one()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var request = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100_000, request: request))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<DepositDto>())!.Id));
        ids.Distinct().Count().ShouldBe(1);
        (await Deposits(client)).Count.ShouldBe(1);
    }

    // Pedido repetido depois de o aporte ser excluído devolve o original: não cria outro nem o ressuscita (a lista segue vazia).
    [Fact]
    public async Task A_repeated_request_after_the_deposit_was_deleted_returns_the_original_and_does_not_bring_it_back()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var request = Guid.NewGuid();
        var deposit = await CreateDeposit(client, null, 100_000, request: request);
        (await client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}")).EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100_000, request: request));

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<DepositDto>())!.Id.ShouldBe(deposit.Id);
        (await Deposits(client)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0L, 1_000_000L, "2026-07-21", null)] // valor zero
    [InlineData(-100L, 1_000_000L, "2026-07-21", null)]
    [InlineData(100_000_000_001L, 1_000_000L, "2026-07-21", null)] // acima do teto
    [InlineData(100_000L, 99L, "2026-07-21", null)] // percentual abaixo de 0,01%
    [InlineData(100_000L, 5_000_001L, "2026-07-21", null)] // acima de 500%
    [InlineData(100_000L, 0L, "2026-07-21", null)]
    [InlineData(100_000L, 1_000_000L, "2099-01-01", null)] // data no futuro
    [InlineData(100_000L, 1_000_000L, "2026-07-21", "2026-07-21")] // vence no dia da aplicação
    [InlineData(100_000L, 1_000_000L, "2026-07-21", "2026-01-01")] // vence antes
    public async Task An_invalid_deposit_is_refused_and_nothing_is_created(long amount, long percent, string date, string? maturity)
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(
            null, amount, percent: percent, date: DateOnly.Parse(date), maturity: maturity is null ? null : DateOnly.Parse(maturity)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Deposits(client)).ShouldBeEmpty();
    }

    // IPCA, IPCA+ e produtos que ainda não existem (debênture; a LCI e a LCA entraram na R.13) não são aceitos: nunca um número que pareça certo
    // (docs/renda-fixa.md, 2.5).
    [Theory]
    [InlineData("Debenture", "Cdi", "Unknown")]
    [InlineData("Cdb", "Ipca", "Unknown")]
    [InlineData("Cdb", "IpcaPlus", "Unknown")]
    [InlineData("Cdb", "Cdi", "Whenever")]
    [InlineData("Cdb", "Cdi", "7")]
    public async Task A_product_or_index_the_engine_does_not_know_is_refused(string product, string index, string liquidity)
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", new
        {
            goalId = (Guid?)null, clientRequestId = Guid.NewGuid(), depositDate = Day, amountCents = 100_000L, productKind = product,
            indexKind = index, indexPercentE4 = 1_000_000L, maturityDate = (DateOnly?)null, liquidity, note = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Deposits(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deposit_in_a_goal_that_does_not_exist_is_not_found()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(Guid.NewGuid(), 100_000));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Sem taxa não há rendimento: antes do primeiro dia do histórico do CDI é recusado, nunca tratado como zero.
    [Fact]
    public async Task A_deposit_before_the_first_rate_of_the_cdi_is_refused_and_on_that_day_is_accepted()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        await SetFirstRateAt(factory, Day);

        var early = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100_000, date: Day.AddDays(-1)));
        var onTheDay = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100_000, date: Day));

        await ShouldBeProblem(early, HttpStatusCode.BadRequest, "BeforeFirstRate");
        onTheDay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Deposits(client)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_any_rate_loaded_yet_the_date_is_not_checked()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        await ClearRates(factory);

        var response = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100_000, date: new DateOnly(2000, 1, 3)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deposit_contract_is_edited_and_a_refused_edit_changes_nothing()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var deposit = await CreateDeposit(client, goal.Id, 609_731);

        var edited = await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(amount: 700_000, percent: 1_100_000, liquidity: "Daily"));
        var refused = await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(amount: 0));
        var missing = await client.PutAsJsonAsync($"/fixed-income-deposits/{Guid.NewGuid()}", UpdateBody());

        edited.EnsureSuccessStatusCode();
        var after = (await edited.Content.ReadFromJsonAsync<DepositDto>())!;
        (after.AmountCents, after.IndexPercentE4, after.Liquidity, after.GoalId).ShouldBe((700_000L, 1_100_000L, "Daily", goal.Id));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Deposits(client)).Single().AmountCents.ShouldBe(700_000);
    }

    [Fact]
    public async Task Editing_a_deposit_before_the_first_rate_is_refused()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var deposit = await CreateDeposit(client, null, 100_000);
        await SetFirstRateAt(factory, Day);

        var response = await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(date: Day.AddDays(-5)));

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "BeforeFirstRate");
        (await Deposits(client)).Single().DepositDate.ShouldBe(Day);
    }

    // O log diz o que mudou, nunca o valor.
    [Fact]
    public async Task The_log_of_an_edit_names_the_fields_and_never_the_values()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var deposit = await CreateDeposit(client, null, 609_731);

        (await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(amount: 777_777, percent: 1_100_000))).EnsureSuccessStatusCode();

        var entry = _logs.Entries.Single(e => e.EventId.Name == "DepositEdited");
        entry["Fields"].ShouldBe("amount,rate");
        entry.Message.ShouldNotContain("777777");
        _logs.Entries.Where(e => (e.EventId.Name ?? "").StartsWith("Deposit") || (e.EventId.Name ?? "").StartsWith("Goal"))
            .ShouldAllBe(e => !e.Message.Contains("609731") && !e.Message.Contains("609.731"));
    }

    // ---- Mover entre caixas ----

    [Fact]
    public async Task A_deposit_moves_between_goals_and_out_of_all_without_touching_its_contract()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var casamento = await CreateGoal(client, "Casamento");
        var viagem = await CreateGoal(client, "Viagem");
        var deposit = await CreateDeposit(client, casamento.Id, 609_731, percent: 1_050_000);

        var toViagem = await Move(client, deposit.Id, viagem.Id);
        var toNone = await Move(client, deposit.Id, null);

        toViagem.GoalId.ShouldBe(viagem.Id);
        toNone.GoalId.ShouldBeNull();
        (toNone with { GoalId = deposit.GoalId }).ShouldBe(deposit);
        var all = await Goals(client);
        all.Goals.ShouldAllBe(g => g.DepositCount == 0);
        all.Unassigned.ShouldBe(new UnassignedDto(1, 609_731));
        _logs.Entries.Count(e => e.EventId.Name == "DepositMoved").ShouldBe(2);
    }

    [Fact]
    public async Task Moving_to_a_goal_or_a_deposit_that_does_not_exist_is_not_found()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var deposit = await CreateDeposit(client, null, 100_000);

        (await client.PatchAsJsonAsync($"/fixed-income-deposits/{deposit.Id}/goal", new { goalId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync($"/fixed-income-deposits/{Guid.NewGuid()}/goal", new { goalId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Excluir e desfazer um aporte ----

    [Fact]
    public async Task A_deleted_deposit_disappears_from_the_lists_and_the_totals_and_comes_back()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var deposit = await CreateDeposit(client, goal.Id, 250_000, percent: 1_100_000);

        (await client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Deposits(client)).ShouldBeEmpty();
        (await Goals(client)).Goals.Single().ShouldBe(goal);

        var restored = await client.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null);
        restored.EnsureSuccessStatusCode();
        (await restored.Content.ReadFromJsonAsync<DepositDto>())!.ShouldBe(deposit);
        (await Goals(client)).Goals.Single().ShouldBe(goal with { DepositCount = 1, PrincipalCents = 250_000 });
    }

    [Fact]
    public async Task Restoring_a_deposit_whose_goal_was_deleted_meanwhile_brings_it_back_loose()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var deposit = await CreateDeposit(client, goal.Id, 250_000);
        (await client.DeleteAsync($"/fixed-income-deposits/{deposit.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/savings-goals/{goal.Id}")).EnsureSuccessStatusCode();

        var restored = await client.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null);

        (await restored.Content.ReadFromJsonAsync<DepositDto>())!.GoalId.ShouldBeNull();
        (await Goals(client)).Unassigned.ShouldBe(new UnassignedDto(1, 250_000));
    }

    // ---- Excluir um objetivo: a caixa não apaga o que tem dentro sem perguntar ----

    [Fact]
    public async Task Deleting_a_goal_with_deposits_requires_a_choice_and_changes_nothing_without_it()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        await CreateDeposit(client, goal.Id, 100_000);
        await CreateDeposit(client, goal.Id, 200_000);

        var response = await client.DeleteAsync($"/savings-goals/{goal.Id}");

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "DepositsChoiceRequired");
        (await Goals(client)).Goals.Single().ShouldBe(goal with { DepositCount = 2, PrincipalCents = 300_000 });
    }

    [Fact]
    public async Task Deleting_a_goal_without_deposits_needs_no_choice_and_an_invalid_choice_is_refused()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var empty = await CreateGoal(client, "Vazia");
        var other = await CreateGoal(client, "Outra");

        (await client.DeleteAsync($"/savings-goals/{other.Id}?deposits=talvez")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var deleted = await client.DeleteAsync($"/savings-goals/{empty.Id}");

        deleted.EnsureSuccessStatusCode();
        var result = (await deleted.Content.ReadFromJsonAsync<DeletedGoalDto>())!;
        result.Id.ShouldBe(empty.Id);
        result.DetachedDepositIds.ShouldBeEmpty();
        result.DeletedDepositIds.ShouldBeEmpty();
        (await Goals(client)).Goals.Select(g => g.Name).ShouldBe(["Outra"]);
        (await client.DeleteAsync($"/savings-goals/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_goal_keeping_the_investments_leaves_them_loose_with_their_history()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var first = await CreateDeposit(client, goal.Id, 100_000, percent: 1_050_000, date: new DateOnly(2026, 7, 21));
        var second = await CreateDeposit(client, goal.Id, 200_000, date: new DateOnly(2026, 8, 5));

        var response = await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=keep");

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<DeletedGoalDto>())!;
        result.DetachedDepositIds.Order().ShouldBe(new[] { first.Id, second.Id }.Order());
        result.DeletedDepositIds.ShouldBeEmpty();
        var all = await Goals(client);
        all.Goals.ShouldBeEmpty();
        all.Unassigned.ShouldBe(new UnassignedDto(2, 300_000));
        var loose = await Deposits(client, "none");
        loose.Select(d => d with { GoalId = goal.Id }).ShouldBe([second, first]); // só a caixa mudou
        _logs.Entries.Single(e => e.EventId.Name == "GoalDeleted")["DetachedDeposits"].ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_a_goal_with_its_investments_removes_them_together()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var kept = await CreateDeposit(client, null, 5_000);
        var inside = await CreateDeposit(client, goal.Id, 100_000);

        var response = await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=delete");

        var result = (await response.Content.ReadFromJsonAsync<DeletedGoalDto>())!;
        result.DeletedDepositIds.ShouldBe([inside.Id]);
        result.DetachedDepositIds.ShouldBeEmpty();
        (await Goals(client)).Goals.ShouldBeEmpty();
        (await Deposits(client)).Select(d => d.Id).ShouldBe([kept.Id]); // o solto não é tocado
    }

    [Fact]
    public async Task Undoing_a_delete_that_removed_everything_brings_the_goal_and_its_deposits_back_as_they_were()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento", "festa", 5_000_000, 1_000_000);
        var deposit = await CreateDeposit(client, goal.Id, 100_000, percent: 1_100_000, note: "CDB");
        (await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=delete")).EnsureSuccessStatusCode();

        var restored = await client.PostAsync($"/savings-goals/{goal.Id}/restore", null);

        restored.EnsureSuccessStatusCode();
        (await restored.Content.ReadFromJsonAsync<GoalDto>())!.ShouldBe(goal with { DepositCount = 1, PrincipalCents = 100_000 });
        (await Deposits(client)).ShouldBe([deposit]);
    }

    [Fact]
    public async Task Undoing_a_delete_that_kept_the_investments_puts_them_back_in_the_box_when_told_which()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var deposit = await CreateDeposit(client, goal.Id, 100_000);
        var deleted = (await (await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=keep")).Content.ReadFromJsonAsync<DeletedGoalDto>())!;

        var restored = await client.PostAsJsonAsync($"/savings-goals/{goal.Id}/restore", new { detachedDepositIds = deleted.DetachedDepositIds });

        restored.EnsureSuccessStatusCode();
        (await restored.Content.ReadFromJsonAsync<GoalDto>())!.ShouldBe(goal with { DepositCount = 1, PrincipalCents = 100_000 });
        (await Deposits(client, goal.Id.ToString())).Single().Id.ShouldBe(deposit.Id);
        _logs.Entries.Single(e => e.EventId.Name == "GoalRestored")["RelinkedDeposits"].ShouldBe(1);
    }

    [Fact]
    public async Task Undoing_without_the_ids_brings_back_only_the_empty_box()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        await CreateDeposit(client, goal.Id, 100_000);
        (await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=keep")).EnsureSuccessStatusCode();

        (await client.PostAsync($"/savings-goals/{goal.Id}/restore", null)).EnsureSuccessStatusCode();

        (await Goals(client)).Goals.Single().DepositCount.ShouldBe(0);
        (await Deposits(client, "none")).Count.ShouldBe(1);
    }

    // O que a pessoa já pôs em outra caixa nesse meio-tempo fica onde está.
    [Fact]
    public async Task Undoing_does_not_pull_back_an_investment_the_person_already_moved_elsewhere()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var casamento = await CreateGoal(client, "Casamento");
        var viagem = await CreateGoal(client, "Viagem");
        var moved = await CreateDeposit(client, casamento.Id, 100_000);
        var stayed = await CreateDeposit(client, casamento.Id, 200_000);
        var deleted = (await (await client.DeleteAsync($"/savings-goals/{casamento.Id}?deposits=keep")).Content.ReadFromJsonAsync<DeletedGoalDto>())!;
        await Move(client, moved.Id, viagem.Id);

        (await client.PostAsJsonAsync($"/savings-goals/{casamento.Id}/restore", new { detachedDepositIds = deleted.DetachedDepositIds }))
            .EnsureSuccessStatusCode();

        (await Deposits(client, viagem.Id.ToString())).Single().Id.ShouldBe(moved.Id);
        (await Deposits(client, casamento.Id.ToString())).Single().Id.ShouldBe(stayed.Id);
    }

    [Fact]
    public async Task Undoing_a_delete_when_the_name_was_taken_meanwhile_is_a_conflict_and_changes_nothing()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        await CreateDeposit(client, goal.Id, 100_000);
        (await client.DeleteAsync($"/savings-goals/{goal.Id}?deposits=delete")).EnsureSuccessStatusCode();
        await CreateGoal(client, "Casamento");

        var response = await client.PostAsync($"/savings-goals/{goal.Id}/restore", null);

        await ShouldBeProblem(response, HttpStatusCode.Conflict, "GoalNameTaken");
        (await Deposits(client)).ShouldBeEmpty();
        (await client.PostAsync($"/savings-goals/{Guid.NewGuid()}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Isolamento ----

    [Fact]
    public async Task One_user_never_sees_or_touches_the_goals_and_deposits_of_another()
    {
        await using var factory = await FactoryAsync();
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bruno = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(alice, "Casamento");
        var deposit = await CreateDeposit(alice, goal.Id, 100_000);
        var brunoGoal = await CreateGoal(bruno, "Viagem");
        var brunoDeposit = await CreateDeposit(bruno, null, 7_000);

        (await Goals(bruno)).Goals.Select(g => g.Id).ShouldBe([brunoGoal.Id]);
        (await Deposits(bruno)).Select(d => d.Id).ShouldBe([brunoDeposit.Id]);
        (await Deposits(bruno, goal.Id.ToString())).ShouldBeEmpty();

        (await bruno.PutAsJsonAsync($"/savings-goals/{goal.Id}", GoalBody("Invadido"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.DeleteAsync($"/savings-goals/{goal.Id}?deposits=delete")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PostAsync($"/savings-goals/{goal.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(amount: 1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.DeleteAsync($"/fixed-income-deposits/{deposit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PostAsync($"/fixed-income-deposits/{deposit.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PatchAsJsonAsync($"/fixed-income-deposits/{deposit.Id}/goal", new { goalId = brunoGoal.Id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nem pôr o que é dele na caixa dos outros, nem criar na caixa dos outros.
        (await bruno.PatchAsJsonAsync($"/fixed-income-deposits/{brunoDeposit.Id}/goal", new { goalId = goal.Id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruno.PostAsJsonAsync("/fixed-income-deposits", DepositBody(goal.Id, 100))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // O mesmo clientRequestId de outra pessoa não devolve o aporte dela.
        var sameRequest = await bruno.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100, request: deposit.Id));
        (await sameRequest.Content.ReadFromJsonAsync<DepositDto>())!.Id.ShouldNotBe(deposit.Id);

        (await Goals(alice)).Goals.Single().ShouldBe(goal with { DepositCount = 1, PrincipalCents = 100_000 });
        (await Deposits(alice)).ShouldBe([deposit]);
    }

    [Fact]
    public async Task The_same_name_may_exist_for_two_different_users()
    {
        await using var factory = await FactoryAsync();
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bruno = await factory.CreateAuthenticatedClientAsync();

        await CreateGoal(alice, "Casamento");
        (await bruno.PostAsJsonAsync("/savings-goals", GoalBody("Casamento"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Everything_requires_a_session()
    {
        await using var factory = await FactoryAsync();
        var anonymous = factory.CreateHttpsClient();

        (await anonymous.GetAsync("/savings-goals")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/fixed-income-deposits")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/savings-goals", GoalBody("x"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/fixed-income-deposits", DepositBody(null, 100))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Concorrência ----

    // xmin: quem tinha a linha na mão antes de outro pedido mexer nela não grava por cima (o 409 do ProblemExceptionHandler).
    [Fact]
    public async Task Goals_and_deposits_carry_the_row_version_token()
    {
        await using var factory = await FactoryAsync();
        var client = await factory.CreateAuthenticatedClientAsync();
        var goal = await CreateGoal(client, "Casamento");
        var deposit = await CreateDeposit(client, goal.Id, 100_000);
        var userId = await UserIdOf(factory, goal.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopedUser>().UserId = userId;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staleGoal = await db.SavingsGoals.SingleAsync(g => g.Id == goal.Id);
        var staleDeposit = await db.FixedIncomeDeposits.SingleAsync(d => d.Id == deposit.Id);

        (await client.PutAsJsonAsync($"/savings-goals/{goal.Id}", GoalBody("Outro nome"))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/fixed-income-deposits/{deposit.Id}", UpdateBody(amount: 5))).EnsureSuccessStatusCode();
        staleGoal.Update("Mudei por cima", null, null, null);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        staleDeposit.MoveTo(null);
        db.Attach(staleDeposit).State = EntityState.Modified;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    // ---- Apoio ----

    // A tabela de taxas é global e as outras classes de teste a deixam com o que precisavam: cada teste daqui começa com ela vazia
    // (sem taxa carregada a data do aporte não é conferida) e põe a sua quando a regra é o que se testa.
    private async Task<PrismaApiFactory> FactoryAsync()
    {
        var factory = new PrismaApiFactory(
            postgres.ConnectionString, new Dictionary<string, string> { ["Rates:Sync:Enabled"] = "false" }, null, _logs);
        await ClearRates(factory);
        return factory;
    }

    private static object GoalBody(string name) =>
        new { name, description = (string?)null, targetCents = (long?)null, defaultIndexPercentE4 = (long?)null };

    private static object DepositBody(
        Guid? goalId, long amountCents, long percent = 1_000_000, DateOnly? date = null, DateOnly? maturity = null,
        string liquidity = "Unknown", string? note = null, Guid? request = null) =>
        new
        {
            goalId, clientRequestId = request ?? Guid.NewGuid(), depositDate = date ?? Day, amountCents, productKind = "Cdb",
            indexKind = "Cdi", indexPercentE4 = percent, maturityDate = maturity, liquidity, note,
        };

    private static object UpdateBody(
        long amount = 100_000, long percent = 1_000_000, DateOnly? date = null, string liquidity = "Unknown") =>
        new
        {
            depositDate = date ?? Day, amountCents = amount, productKind = "Cdb", indexKind = "Cdi", indexPercentE4 = percent,
            maturityDate = (DateOnly?)null, liquidity, note = (string?)null,
        };

    private static async Task<GoalDto> CreateGoal(
        HttpClient client, string name, string? description = null, long? target = null, long? percent = null)
    {
        var response = await client.PostAsJsonAsync("/savings-goals",
            new { name, description, targetCents = target, defaultIndexPercentE4 = percent });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GoalDto>())!;
    }

    private static async Task<DepositDto> CreateDeposit(
        HttpClient client, Guid? goalId, long amount, long percent = 1_000_000, DateOnly? date = null, string? note = null,
        Guid? request = null)
    {
        var response = await client.PostAsJsonAsync("/fixed-income-deposits", DepositBody(goalId, amount, percent, date, note: note, request: request));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!;
    }

    private static async Task<DepositDto> Move(HttpClient client, Guid depositId, Guid? goalId)
    {
        var response = await client.PatchAsJsonAsync($"/fixed-income-deposits/{depositId}/goal", new { goalId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DepositDto>())!;
    }

    private static async Task<GoalsDto> Goals(HttpClient client) => (await client.GetFromJsonAsync<GoalsDto>("/savings-goals"))!;

    private static async Task<List<DepositDto>> Deposits(HttpClient client, string? goal = null) =>
        (await client.GetFromJsonAsync<List<DepositDto>>(goal is null ? "/fixed-income-deposits" : $"/fixed-income-deposits?goal={goal}"))!;

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    // A tabela de taxas é global: cada teste que depende dela a deixa como precisa.
    private static async Task ClearRates(PrismaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReferenceRates.ExecuteDeleteAsync();
    }

    private static async Task SetFirstRateAt(PrismaApiFactory factory, DateOnly first)
    {
        await ClearRates(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ReferenceRates.Add(ReferenceRate.From(ReferenceRateSeries.Cdi, new DailyRate(first, 50_000), "bcb-sgs-12", DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> UserIdOf(PrismaApiFactory factory, Guid goalId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<Guid>($"SELECT user_id AS \"Value\" FROM savings_goals WHERE id = {goalId}").SingleAsync();
    }
}
