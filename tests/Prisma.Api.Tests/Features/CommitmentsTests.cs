using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 2.8 (docs/fase-2.md, 2.6), com o exemplo da regra. Hoje é 15/10/2026 em São Paulo.
[Collection(ApiCollection.Name)]
public sealed class CommitmentsTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record EntryDto(Guid Id, Guid? StatementId, int? InstallmentNumber, Guid? InstallmentPurchaseId);

    private sealed record ItemDto(
        Guid TransactionId, Guid PurchaseId, string Description, Guid? CategoryId, Guid AccountId,
        int InstallmentNumber, int InstallmentCount, long AmountCents, DateOnly PurchaseDate);

    private sealed record InheritedDto(string Month, long ExpenseCents, long InheritedCents, long? DecidedInMonthCents, List<ItemDto> Items);

    private sealed record MonthDto(string Month, long ExpenseCents);

    private sealed record CommittedDto(List<MonthDto> Months, string? LastInstallmentMonth);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<T> Created<T>(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed class Scenario(HttpClient client)
    {
        public Guid Visa { get; private set; }
        public Guid Itau { get; private set; }

        public async Task Accounts()
        {
            Visa = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
                new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 }))).Id;
            Itau = (await Created<IdDto>(client.PostAsJsonAsync("/accounts",
                new { name = "Itaú", type = "Checking", initialBalanceCents = 0 }))).Id;
        }

        public Task<List<EntryDto>> Card(string description, long cents, string date, int installments = 1, string type = "Expense") =>
            Created<List<EntryDto>>(client.PostAsJsonAsync("/transactions", new
            {
                accountId = Visa, type, amountCents = cents, purchaseDate = date, method = "Credit", installments, description,
            }));

        public Task<List<EntryDto>> Pix(string description, long cents, string date) =>
            Created<List<EntryDto>>(client.PostAsJsonAsync("/transactions", new
            {
                accountId = Itau, type = "Expense", amountCents = cents, purchaseDate = date, method = "Pix", description,
            }));

        public Task Pay(Guid? statementId, string date) =>
            Created<List<IdDto>>(client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = Itau, date, method = "Boleto" }));
    }

    // O exemplo inteiro da seção 2.6.
    private static async Task SpecExample(Scenario s, HttpClient client)
    {
        await s.Accounts();
        await s.Card("TV", 400000, "2026-06-10", installments: 10);
        await s.Card("Passagem", 360000, "2026-08-20", installments: 6);
        var groceries = await s.Card("Mercado", 30000, "2026-08-02", installments: 3);
        await s.Card("Tênis", 60000, "2026-09-15", installments: 3);
        await s.Pix("Feira", 25000, "2026-10-03");
        await s.Card("iFood", 8000, "2026-09-28");
        await s.Card("Estorno", 5000, "2026-10-08", type: "Refund");

        // O Relógio é excluído antes de pagar a fatura de outubro: a 2/3 dele venceria em outubro.
        var watch = await s.Card("Relógio", 90000, "2026-08-20", installments: 3);
        (await client.DeleteAsync($"/installment-purchases/{watch[0].InstallmentPurchaseId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // Pagar a fatura de outubro é transferência: não entra em nada.
        await s.Pay(groceries[1].StatementId, "2026-10-05");
    }

    [Fact]
    public async Task Spec_example_inherited_installments_of_october()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var s = new Scenario(client);
        await SpecExample(s, client);

        var october = (await client.GetFromJsonAsync<InheritedDto>("/dashboard/inherited?month=2026-10"))!;

        october.ExpenseCents.ShouldBe(155000);
        october.InheritedCents.ShouldBe(110000);
        october.DecidedInMonthCents.ShouldBe(45000);
        october.Items.Select(i => (i.Description, i.InstallmentNumber, i.InstallmentCount, i.AmountCents, i.PurchaseDate)).ShouldBe(
        [
            ("Passagem", 2, 6, 60000L, new DateOnly(2026, 8, 20)),
            ("TV", 4, 10, 40000L, new DateOnly(2026, 6, 10)),
            ("Mercado", 2, 3, 10000L, new DateOnly(2026, 8, 2)),
        ]);
        october.Items.ShouldAllBe(i => i.AccountId == s.Visa);

        // O "Saiu" é o mesmo do resumo do mês.
        (await client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-10"))!.ExpenseCents.ShouldBe(155000);
    }

    private sealed record SummaryDto(long ExpenseCents);

    [Fact]
    public async Task Spec_example_committed_months_from_november_and_the_last_installment()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        await SpecExample(new Scenario(client), client);

        var committed = (await client.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!;

        committed.Months.Select(m => (m.Month, m.ExpenseCents)).ShouldBe(
        [
            ("2026-11", 133000L), ("2026-12", 120000L), ("2027-01", 100000L),
            ("2027-02", 100000L), ("2027-03", 40000L), ("2027-04", 40000L),
        ]);
        committed.LastInstallmentMonth.ShouldBe("2027-04");
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_and_an_empty_account_has_nothing()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var other = await factory.CreateAuthenticatedClientAsync();
        var s = new Scenario(client);
        await s.Accounts();
        await s.Card("TV", 400000, "2026-06-10", installments: 10);

        var inherited = (await other.GetFromJsonAsync<InheritedDto>("/dashboard/inherited?month=2026-10"))!;
        inherited.InheritedCents.ShouldBe(0);
        inherited.Items.ShouldBeEmpty();
        inherited.DecidedInMonthCents.ShouldBe(0);

        var committed = (await other.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!;
        committed.Months.ShouldAllBe(m => m.ExpenseCents == 0);
        committed.LastInstallmentMonth.ShouldBeNull();

        (await client.GetAsync("/dashboard/inherited?month=outubro")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
