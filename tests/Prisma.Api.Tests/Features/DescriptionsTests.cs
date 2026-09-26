using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 2.15 (docs/fase-2.md, 2.8), com o exemplo da regra. Hoje é 25/09/2026 em São Paulo.
[Collection(ApiCollection.Name)]
public sealed class DescriptionsTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record CategoryNodeDto(Guid Id, string Name);

    private sealed record ItemDto(
        string Description, string Type, Guid? CategoryId, Guid AccountId, string Method, long LastAmountCents, int Count,
        DateOnly LastUsedOn);

    private static readonly FakeClock September25 = new(new DateTime(2026, 9, 25, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<T> Created<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record Example(Guid Nubank, Guid Itau, Guid Food, Guid Health, Guid Salary, Guid Shopping);

    private static async Task<Example> SpecExample(HttpClient client)
    {
        var nubank = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Nubank", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 }))).Id;
        var itau = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 0 }))).Id;

        var tree = (await client.GetFromJsonAsync<List<CategoryNodeDto>>("/categories"))!;
        Guid Root(string name) => tree.Single(c => c.Name == name).Id;
        var (food, leisure, health, salary, shopping) =
            (Root("Alimentação"), Root("Lazer"), Root("Saúde"), Root("Salário"), Root("Compras"));

        Task<List<IdDto>> Entry(Guid account, string type, string? description, long cents, string date, string method, Guid? category,
            int installments = 1) =>
            Created<List<IdDto>>(client.PostAsJsonAsync("/transactions", new
            {
                accountId = account, type, amountCents = cents, purchaseDate = date, method, categoryId = category, description,
                installments,
            }));

        await Entry(nubank, "Expense", "iFood", 4290, "2026-09-20", "Credit", food);
        await Entry(itau, "Expense", "ifood ", 3500, "2026-09-10", "Pix", leisure);
        await Entry(itau, "Expense", "Farmácia São João", 6480, "2026-09-05", "Pix", health);
        await Entry(itau, "Expense", "farmacia  sao joao", 3000, "2026-08-01", "Pix", health);
        await Entry(itau, "Income", "Salário", 820000, "2026-09-05", "Pix", salary);
        await Entry(nubank, "Expense", "Notebook", 600000, "2026-03-15", "Credit", shopping, installments: 10);
        await Entry(nubank, "Refund", "Estorno: iFood", 1000, "2026-09-12", "Credit", food);
        // Transferência (um saque para a carteira) não entra.
        var wallet = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Carteira", type = "Cash", initialBalanceCents = 0 }))).Id;
        await Created<List<IdDto>>(client.PostAsJsonAsync("/transfers",
            new { fromAccountId = itau, toAccountId = wallet, amountCents = 50000, date = "2026-09-05", method = "Pix" }));
        var cinema = await Entry(itau, "Expense", "Cinema", 7000, "2026-09-14", "Pix", leisure);
        (await client.DeleteAsync($"/transactions/{cinema[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Entry(itau, "Expense", "Mercado", 20000, "2025-09-24", "Debit", food);
        await Entry(itau, "Expense", null, 1500, "2026-09-18", "Pix", food);

        return new Example(nubank, itau, food, health, salary, shopping);
    }

    [Fact]
    public async Task Spec_example_vocabulary()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: September25);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var e = await SpecExample(client);

        var items = (await client.GetFromJsonAsync<List<ItemDto>>("/transactions/descriptions"))!;

        items.ShouldBe(
        [
            new ItemDto("iFood", "Expense", e.Food, e.Nubank, "Credit", 4290, 2, new DateOnly(2026, 9, 20)),
            new ItemDto("Farmácia São João", "Expense", e.Health, e.Itau, "Pix", 6480, 2, new DateOnly(2026, 9, 5)),
            new ItemDto("Salário", "Income", e.Salary, e.Itau, "Pix", 820000, 1, new DateOnly(2026, 9, 5)),
            new ItemDto("Notebook", "Expense", e.Shopping, e.Nubank, "Credit", 600000, 1, new DateOnly(2026, 3, 15)),
        ]);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: September25);
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();
        await SpecExample(client);

        (await other.GetFromJsonAsync<List<ItemDto>>("/transactions/descriptions"))!.ShouldBeEmpty();
    }
}
