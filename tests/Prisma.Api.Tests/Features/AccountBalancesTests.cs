using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 1.16 (docs/fase-1.md, 2.5), com o exemplo da regra. Hoje é 10/04/2026 em São Paulo.
[Collection(ApiCollection.Name)]
public sealed class AccountBalancesTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record InstallmentDto(Guid Id, Guid? StatementId);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents, long? ProjectedBalanceCents, long? OwedCents, long? AvailableCreditCents);

    private static readonly FakeClock April10 = new(new DateTime(2026, 4, 10, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<T> Created<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<HttpResponseMessage> Entry(HttpClient client, Guid account, string type, long cents, string date) =>
        client.PostAsJsonAsync("/transactions", new { accountId = account, type, amountCents = cents, purchaseDate = date, method = "Pix" });

    private static async Task<Dictionary<Guid, BalanceDto>> Balances(HttpClient client) =>
        (await client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.ToDictionary(b => b.AccountId);

    [Fact]
    public async Task Spec_example_balances_for_accounts_and_card()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: April10);
        using var client = await factory.CreateAuthenticatedClientAsync();

        var checking = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 100000 }))).Id;
        var cash = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Carteira", type = "Cash", initialBalanceCents = 5000 }))).Id;
        var card = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12, creditLimitCents = 500000 }))).Id;

        await Created<List<IdDto>>(Entry(client, checking, "Income", 300000, "2026-04-01"));
        await Created<List<IdDto>>(Entry(client, checking, "Expense", 4590, "2026-04-02"));
        await Created<List<IdDto>>(client.PostAsJsonAsync("/transfers",
            new { fromAccountId = checking, toAccountId = cash, amountCents = 20000, date = "2026-04-03", method = "Pix" }));

        // Excluída: não conta. Futura: só no previsto.
        var deleted = (await Created<List<IdDto>>(Entry(client, checking, "Expense", 99900, "2026-04-04")))[0].Id;
        (await client.DeleteAsync($"/transactions/{deleted}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Created<List<IdDto>>(Entry(client, checking, "Expense", 7000, "2026-04-17"));

        // R$ 300,00 em 3x no dia 10/03: faturas de abril (fecha 05/04), maio e junho.
        var installments = await Created<List<InstallmentDto>>(client.PostAsJsonAsync("/transactions",
            new { accountId = card, type = "Expense", amountCents = 30000, purchaseDate = "2026-03-10", method = "Credit", installments = 3 }));

        var before = await Balances(client);
        before[card].OwedCents.ShouldBe(30000);
        before[card].AvailableCreditCents.ShouldBe(470000);
        before[card].BalanceCents.ShouldBeNull();

        await Created<List<IdDto>>(client.PostAsJsonAsync($"/statements/{installments[0].StatementId}/pay",
            new { fromAccountId = checking, date = "2026-04-10", method = "Boleto" }));

        var after = await Balances(client);
        after[checking].BalanceCents.ShouldBe(365410);
        after[checking].ProjectedBalanceCents.ShouldBe(358410);
        after[checking].OwedCents.ShouldBeNull();
        after[cash].BalanceCents.ShouldBe(25000);
        after[cash].ProjectedBalanceCents.ShouldBe(25000);
        after[card].OwedCents.ShouldBe(20000);
        after[card].AvailableCreditCents.ShouldBe(480000);
    }

    [Fact]
    public async Task Card_without_limit_has_no_available_credit_and_balances_are_per_user()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: April10);
        using var owner = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();

        var card = (await Created<IdDto>(owner.PostAsJsonAsync("/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 }))).Id;
        await Created<List<IdDto>>(owner.PostAsJsonAsync("/transactions",
            new { accountId = card, type = "Expense", amountCents = 4590, purchaseDate = "2026-04-08", method = "Credit" }));
        var othersChecking = (await Created<IdDto>(other.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 1000 }))).Id;

        var mine = await Balances(owner);
        mine.Keys.ShouldBe([card]);
        mine[card].OwedCents.ShouldBe(4590);
        mine[card].AvailableCreditCents.ShouldBeNull();

        var theirs = await Balances(other);
        theirs.Keys.ShouldBe([othersChecking]);
        theirs[othersChecking].BalanceCents.ShouldBe(1000);
    }
}
