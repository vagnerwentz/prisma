using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 2.10 (docs/fase-2.md, 2.7), com o exemplo da regra: setembro contra agosto de 2026.
[Collection(ApiCollection.Name)]
public sealed class SpendingVariationTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record EntryDto(Guid Id, Guid? StatementId, Guid? InstallmentPurchaseId);

    private sealed record CategoryNodeDto(Guid Id, string Name);

    private sealed record LargestDto(Guid TransactionId, string Description, long AmountCents);

    private sealed record PurchaseDto(Guid PurchaseId, string Description, long AmountCents);

    private sealed record ReasonDto(
        string Kind, Guid? CategoryId, string? Name, string? Icon, string? Color, long ChangeCents, LargestDto? Largest,
        List<PurchaseDto> Started, List<PurchaseDto> Ended);

    private sealed record VariationDto(
        string Month, string PreviousMonth, long ExpenseCents, long PreviousExpenseCents, long ChangeCents, List<ReasonDto> Reasons);

    private sealed record SummaryDto(long ExpenseCents);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<T> Created<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    // O exemplo da seção 2.7. Visa fecha dia 26 e vence dia 5: a TV (10/06) vence de julho a abril, a
    // Passagem (20/07) de agosto a janeiro. O resto é Pix no Itaú, na data de caixa do exemplo.
    private static async Task<(Guid Leisure, Guid Health, Guid Food, Guid TicketPurchase)> SpecExample(HttpClient client)
    {
        var visa = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 }))).Id;
        var itau = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 0 }))).Id;

        var tree = (await client.GetFromJsonAsync<List<CategoryNodeDto>>("/categories"))!;
        Guid Root(string name) => tree.Single(c => c.Name == name).Id;
        var (home, leisure, health, food, transport) = (Root("Moradia"), Root("Lazer"), Root("Saúde"), Root("Alimentação"), Root("Transporte"));

        Task<List<EntryDto>> Card(string description, long cents, string date, int installments, Guid category) =>
            Created<List<EntryDto>>(client.PostAsJsonAsync("/transactions", new
            {
                accountId = visa, type = "Expense", amountCents = cents, purchaseDate = date, method = "Credit", installments,
                description, categoryId = category,
            }));

        Task<List<EntryDto>> Pix(string description, long cents, string date, Guid category) =>
            Created<List<EntryDto>>(client.PostAsJsonAsync("/transactions", new
            {
                accountId = itau, type = "Expense", amountCents = cents, purchaseDate = date, method = "Pix", description,
                categoryId = category,
            }));

        await Card("TV", 400000, "2026-06-10", 10, home);
        var ticket = await Card("Passagem", 360000, "2026-07-20", 6, leisure);

        await Pix("Cinema", 10000, "2026-08-08", leisure);
        await Pix("Mercado", 90000, "2026-08-10", food);
        await Pix("iFood", 30000, "2026-08-12", food);
        await Pix("Uber", 30000, "2026-08-14", transport);

        await Pix("Show", 18000, "2026-09-06", leisure);
        await Pix("Cinema", 7000, "2026-09-08", leisure);
        await Pix("Farmácia", 15000, "2026-09-09", health);
        await Pix("Mercado", 80000, "2026-09-10", food);
        await Pix("iFood", 25000, "2026-09-12", food);
        await Pix("Uber", 30000, "2026-09-14", transport);

        // Excluída: não conta. Pagar a fatura de setembro é transferência: não conta.
        var deleted = await Pix("Ingresso", 50000, "2026-09-20", leisure);
        (await client.DeleteAsync($"/transactions/{deleted[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Created<List<IdDto>>(client.PostAsJsonAsync($"/statements/{ticket[1].StatementId}/pay",
            new { fromAccountId = itau, date = "2026-09-05", method = "Boleto" }));

        return (leisure, health, food, ticket[0].InstallmentPurchaseId!.Value);
    }

    [Fact]
    public async Task Spec_example_september_against_august()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var (leisure, health, food, ticket) = await SpecExample(client);

        var variation = (await client.GetFromJsonAsync<VariationDto>("/dashboard/variation?month=2026-09"))!;

        variation.Month.ShouldBe("2026-09");
        variation.PreviousMonth.ShouldBe("2026-08");
        variation.ExpenseCents.ShouldBe(275000);
        variation.PreviousExpenseCents.ShouldBe(260000);
        variation.ChangeCents.ShouldBe(15000);
        variation.Reasons.Select(r => (r.Kind, r.CategoryId, r.Name, r.ChangeCents)).ShouldBe(
        [
            ("Inherited", null, null, 60000L),
            ("Category", leisure, "Lazer", -45000L),
            ("Category", health, "Saúde", 15000L),
            ("Category", food, "Alimentação", -15000L),
        ]);
        variation.Reasons[0].Started.ShouldBe([new PurchaseDto(ticket, "Passagem", 60000)]);
        variation.Reasons[2].Largest!.Description.ShouldBe("Farmácia");
        variation.Reasons[2].Icon.ShouldNotBeNull();
        variation.Reasons[2].Color.ShouldNotBeNull();

        // Os dois "Saiu" são os do resumo de cada mês.
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-09"))!.ExpenseCents.ShouldBe(275000);
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-08"))!.ExpenseCents.ShouldBe(260000);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_and_an_invalid_month_is_rejected()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();
        await SpecExample(client);

        var variation = (await other.GetFromJsonAsync<VariationDto>("/dashboard/variation?month=2026-09"))!;
        variation.ExpenseCents.ShouldBe(0);
        variation.PreviousExpenseCents.ShouldBe(0);
        variation.Reasons.ShouldBeEmpty();

        // Sem mês, o de hoje.
        (await other.GetFromJsonAsync<VariationDto>("/dashboard/variation"))!.Month.ShouldBe("2026-10");
        (await client.GetAsync("/dashboard/variation?month=setembro")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
