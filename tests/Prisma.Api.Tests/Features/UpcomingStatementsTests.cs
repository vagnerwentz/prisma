using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 2.4 (docs/fase-2.md, 2.4), com o exemplo da regra. Hoje é 15/10/2026 em São Paulo.
[Collection(ApiCollection.Name)]
public sealed class UpcomingStatementsTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record EntryDto(Guid Id, Guid? StatementId);

    private sealed record ItemDto(Guid AccountId, string CardName, Guid StatementId, DateOnly DueDate, long TotalCents);

    private sealed record UpcomingDto(long TotalCents, List<ItemDto> Statements);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<T> Created<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> Card(HttpClient client, string name, int closingDay, int dueDay) =>
        (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name, type = "CreditCard", initialBalanceCents = 0, closingDay, dueDay }))).Id;

    private static async Task<List<EntryDto>> Purchase(HttpClient client, Guid card, long cents, string date, int installments = 1) =>
        await Created<List<EntryDto>>(client.PostAsJsonAsync("/transactions",
            new { accountId = card, type = "Expense", amountCents = cents, purchaseDate = date, method = "Credit", installments }));

    private static Task Pay(HttpClient client, Guid? statementId, Guid from, string date) =>
        Created<List<IdDto>>(client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = from, date, method = "Boleto" }));

    private static async Task<UpcomingDto> Upcoming(HttpClient client) =>
        (await client.GetFromJsonAsync<UpcomingDto>("/dashboard/upcoming-statements"))!;

    [Fact]
    public async Task Spec_example_next_unpaid_statement_of_each_active_card()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var checking = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 0 }))).Id;

        // Visa: a fatura de 05/10 paga; a de 05/11 com o sapato.
        var visa = await Card(client, "Visa", closingDay: 26, dueDay: 5);
        await Pay(client, (await Purchase(client, visa, 60000, "2026-09-20"))[0].StatementId, checking, "2026-10-05");
        await Purchase(client, visa, 30000, "2026-10-10");

        // Master: R$ 300,00 em 3x, faturas de 20/10, 20/11 e 20/12. A de 20/10 fechou em 10/10 e já foi paga.
        var master = await Card(client, "Master", closingDay: 10, dueDay: 20);
        var installments = await Purchase(client, master, 30000, "2026-09-15", installments: 3);
        await Pay(client, installments[0].StatementId, checking, "2026-10-15");

        // Elo: a de 15/09 venceu sem pagar; a de 15/10 vence hoje.
        var elo = await Card(client, "Elo", closingDay: 5, dueDay: 15);
        await Purchase(client, elo, 5000, "2026-09-01");
        await Purchase(client, elo, 8000, "2026-09-10");

        // Nubank: a única compra da fatura de 20/10 foi excluída; a de 20/11 tem valor.
        var nubank = await Card(client, "Nubank", closingDay: 10, dueDay: 20);
        var deleted = (await Purchase(client, nubank, 7000, "2026-09-15"))[0].Id;
        (await client.DeleteAsync($"/transactions/{deleted}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Purchase(client, nubank, 4000, "2026-10-12");

        // Inter: inativo, fica de fora mesmo com fatura em aberto.
        var inter = await Card(client, "Inter", closingDay: 26, dueDay: 5);
        await Purchase(client, inter, 99900, "2026-10-10");
        (await client.PatchAsJsonAsync($"/accounts/{inter}", new
        {
            name = "Inter", initialBalanceCents = 0, closingDay = 26, dueDay = 5, creditLimitCents = (long?)null, isActive = false,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var upcoming = await Upcoming(client);

        upcoming.Statements.Select(s => (s.AccountId, s.CardName, s.DueDate, s.TotalCents)).ShouldBe(
        [
            (elo, "Elo", new DateOnly(2026, 10, 15), 8000L),
            (visa, "Visa", new DateOnly(2026, 11, 5), 30000L),
            (master, "Master", new DateOnly(2026, 11, 20), 10000L),
            (nubank, "Nubank", new DateOnly(2026, 11, 20), 4000L),
        ]);
        upcoming.Statements[2].StatementId.ShouldBe(installments[1].StatementId!.Value);
        upcoming.TotalCents.ShouldBe(52000);
    }

    [Fact]
    public async Task Without_cards_it_is_empty_and_each_user_sees_only_their_own()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();

        var visa = await Card(client, "Visa", closingDay: 26, dueDay: 5);
        await Purchase(client, visa, 30000, "2026-10-10");

        (await Upcoming(client)).TotalCents.ShouldBe(30000);
        var empty = await Upcoming(other);
        empty.Statements.ShouldBeEmpty();
        empty.TotalCents.ShouldBe(0);
    }
}
