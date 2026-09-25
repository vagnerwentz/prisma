using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Fase 2 (docs/fase-2.md), com o exemplo da seção 2. Hoje é 15/10/2026: o resgate de 20/10 ainda
// não aconteceu, mas conta, porque o mês inteiro entra no resumo.
[Collection(ApiCollection.Name)]
public sealed class DashboardTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record EntryDto(Guid Id, Guid? StatementId);

    private sealed record CategoryNodeDto(Guid Id, string Name, List<CategoryNodeDto>? Subcategories);

    private sealed record CategoryTotalDto(Guid? CategoryId, string? Name, string? Icon, string? Color, long AmountCents);

    private sealed record ListedDto(Guid Id, long AmountCents);

    private sealed record SummaryDto(
        string Month, long IncomeCents, long ExpenseCents, long CardExpenseCents, long LeftoverCents, long InvestedCents);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private sealed class Example(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid DeletedExpense { get; private set; }
        public Guid Housing { get; private set; }
        public Guid Food { get; private set; }
        public Guid Shopping { get; private set; }

        public static async Task<Example> Create(PrismaApiFactory factory)
        {
            var e = new Example(await factory.CreateAuthenticatedClientAsync());
            e.Checking = await e.Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            var cash = await e.Account(new { name = "Carteira", type = "Cash", initialBalanceCents = 0 });
            var treasury = await e.Account(new { name = "Tesouro", type = "Investment", initialBalanceCents = 0 });
            var card = await e.Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });

            var tree = (await e.Client.GetFromJsonAsync<List<CategoryNodeDto>>("/categories"))!;
            Guid Root(string name) => tree.Single(c => c.Name == name).Id;
            Guid Sub(string root, string name) => tree.Single(c => c.Name == root).Subcategories!.Single(c => c.Name == name).Id;
            e.Housing = Root("Moradia");
            e.Food = Root("Alimentação");
            e.Shopping = Root("Compras");
            var salary = tree.Single(c => c.Name == "Salário").Id;

            await e.Entry(e.Checking, "Income", 800000, "2026-10-05", "Pix", salary);
            await e.Entry(e.Checking, "Expense", 250000, "2026-10-10", "Pix", Sub("Moradia", "Aluguel"));
            await e.Entry(e.Checking, "Expense", 80000, "2026-10-12", "Debit", Sub("Alimentação", "Mercado"));
            await e.Entry(e.Checking, "Expense", 10000, "2026-10-15", "Pix");           // farmácia, sem categoria
            var dinner = await e.Entry(card, "Expense", 60000, "2026-09-20", "Credit", Sub("Alimentação", "Restaurante")); // vence 05/10
            await e.Entry(card, "Expense", 30000, "2026-10-10", "Credit", e.Shopping);   // sapato: fatura vence 05/11
            e.DeletedExpense = (await e.Entry(e.Checking, "Expense", 99900, "2026-10-09", "Pix", Root("Lazer"))).Id;
            (await e.Client.DeleteAsync($"/transactions/{e.DeletedExpense}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await e.Created(e.Client.PostAsJsonAsync($"/statements/{dinner.StatementId}/pay",
                new { fromAccountId = e.Checking, date = "2026-10-05", method = "Boleto" }));
            await e.Transfer(e.Checking, treasury, 150000, "2026-10-06");                // aporte
            await e.Transfer(treasury, e.Checking, 50000, "2026-10-20");                 // resgate
            await e.Transfer(e.Checking, cash, 20000, "2026-10-08");                     // saque
            return e;
        }

        private async Task<Guid> Account(object body) =>
            (await (await Created(Client.PostAsJsonAsync("/accounts", body))).Content.ReadFromJsonAsync<IdDto>())!.Id;

        private async Task<EntryDto> Entry(Guid account, string type, long cents, string date, string method, Guid? categoryId = null) =>
            (await (await Created(Client.PostAsJsonAsync("/transactions",
                new { accountId = account, type, amountCents = cents, purchaseDate = date, method, categoryId }))).Content
                .ReadFromJsonAsync<List<EntryDto>>())![0];

        private Task Transfer(Guid from, Guid to, long cents, string date) =>
            Created(Client.PostAsJsonAsync("/transfers", new { fromAccountId = from, toAccountId = to, amountCents = cents, date, method = "Pix" }));

        private async Task<HttpResponseMessage> Created(Task<HttpResponseMessage> request)
        {
            var response = await request;
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return response;
        }

        public void Dispose() => Client.Dispose();
    }

    private static async Task<SummaryDto> Summary(HttpClient client, string? month = null) =>
        (await client.GetFromJsonAsync<SummaryDto>(month is null ? "/dashboard/summary" : $"/dashboard/summary?month={month}"))!;

    [Fact]
    public async Task Spec_example_summary_for_october_and_november()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        // O pagamento da fatura (R$ 600,00) não duplica o jantar; o saque não é despesa.
        (await Summary(e.Client, "2026-10")).ShouldBe(new SummaryDto("2026-10", 800000, 400000, 60000, 400000, 100000));
        (await Summary(e.Client, "2026-11")).ShouldBe(new SummaryDto("2026-11", 0, 30000, 30000, -30000, 0));
        (await Summary(e.Client, "2026-09")).ShouldBe(new SummaryDto("2026-09", 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Without_month_it_is_the_current_month_and_a_bad_month_is_400()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var client = await factory.CreateAuthenticatedClientAsync();

        (await Summary(client)).Month.ShouldBe("2026-10");

        var response = await client.GetAsync("/dashboard/summary?month=10-2026");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe("Informe o mês no formato aaaa-mm.");
    }

    [Fact]
    public async Task A_deleted_expense_leaves_the_summary_and_comes_back_when_restored()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        (await e.Client.PostAsync($"/transactions/{e.DeletedExpense}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Summary(e.Client, "2026-10")).ExpenseCents.ShouldBe(499900);

        (await e.Client.DeleteAsync($"/transactions/{e.DeletedExpense}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Summary(e.Client, "2026-10")).ExpenseCents.ShouldBe(400000);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_numbers()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        using var other = await factory.CreateAuthenticatedClientAsync();

        (await Summary(other, "2026-10")).ShouldBe(new SummaryDto("2026-10", 0, 0, 0, 0, 0));
        (await Summary(e.Client, "2026-10")).IncomeCents.ShouldBe(800000);
    }

    private static async Task<List<CategoryTotalDto>> Categories(HttpClient client, string month) =>
        (await client.GetFromJsonAsync<List<CategoryTotalDto>>($"/dashboard/categories?month={month}"))!;

    [Fact]
    public async Task Spec_example_expenses_by_root_category()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        // Aluguel soma em Moradia; mercado e jantar (fatura de outubro) em Alimentação; a despesa
        // excluída (Lazer) não aparece; a farmácia fica em "sem categoria" (id e nome nulos).
        var october = await Categories(e.Client, "2026-10");
        october.Select(c => (c.CategoryId, c.Name, c.AmountCents)).ShouldBe(
        [
            (e.Housing, "Moradia", 250000L),
            (e.Food, "Alimentação", 140000L),
            (null, null, 10000L),
        ]);
        october[0].Icon.ShouldNotBeNull();
        october[0].Color.ShouldNotBeNull();

        (await Categories(e.Client, "2026-11")).Select(c => (c.CategoryId, c.AmountCents)).ShouldBe([(e.Shopping, 30000L)]);
        (await Categories(e.Client, "2026-09")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Drilling_into_a_category_by_cash_date_adds_up_to_the_dashboard()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        const string october = "from=2026-10-01&to=2026-10-31";

        async Task<List<ListedDto>> List(string filter) =>
            (await e.Client.GetFromJsonAsync<List<ListedDto>>($"/transactions?{october}&{filter}"))!;

        (await List($"dateBasis=Settlement&type=Expense&categoryId={e.Food}")).Sum(t => t.AmountCents).ShouldBe(140000);
        (await List("dateBasis=Settlement&type=Expense&uncategorized=true")).Sum(t => t.AmountCents).ShouldBe(10000);

        // Pela data da compra (a lista de sempre), o jantar de 20/09 fica fora de outubro.
        (await List($"categoryId={e.Food}")).Sum(t => t.AmountCents).ShouldBe(80000);
    }

    [Fact]
    public async Task Categories_of_another_user_are_not_counted()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        using var other = await factory.CreateAuthenticatedClientAsync();

        (await Categories(other, "2026-10")).ShouldBeEmpty();
    }
}
