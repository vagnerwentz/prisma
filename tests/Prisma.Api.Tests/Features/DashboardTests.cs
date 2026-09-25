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

    private sealed record SummaryDto(string Month, long IncomeCents, long ExpenseCents, long LeftoverCents, long InvestedCents);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private sealed class Example(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid DeletedExpense { get; private set; }

        public static async Task<Example> Create(PrismaApiFactory factory)
        {
            var e = new Example(await factory.CreateAuthenticatedClientAsync());
            e.Checking = await e.Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            var cash = await e.Account(new { name = "Carteira", type = "Cash", initialBalanceCents = 0 });
            var treasury = await e.Account(new { name = "Tesouro", type = "Investment", initialBalanceCents = 0 });
            var card = await e.Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });

            await e.Entry(e.Checking, "Income", 800000, "2026-10-05", "Pix");           // salário
            await e.Entry(e.Checking, "Expense", 250000, "2026-10-10", "Pix");          // aluguel
            await e.Entry(e.Checking, "Expense", 80000, "2026-10-12", "Debit");         // mercado
            await e.Entry(e.Checking, "Expense", 10000, "2026-10-15", "Pix");           // farmácia
            var dinner = await e.Entry(card, "Expense", 60000, "2026-09-20", "Credit"); // fatura vence 05/10
            await e.Entry(card, "Expense", 30000, "2026-10-10", "Credit");              // sapato: fatura vence 05/11
            e.DeletedExpense = (await e.Entry(e.Checking, "Expense", 99900, "2026-10-09", "Pix")).Id;
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

        private async Task<EntryDto> Entry(Guid account, string type, long cents, string date, string method) =>
            (await (await Created(Client.PostAsJsonAsync("/transactions",
                new { accountId = account, type, amountCents = cents, purchaseDate = date, method }))).Content
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
        (await Summary(e.Client, "2026-10")).ShouldBe(new SummaryDto("2026-10", 800000, 400000, 400000, 100000));
        (await Summary(e.Client, "2026-11")).ShouldBe(new SummaryDto("2026-11", 0, 30000, -30000, 0));
        (await Summary(e.Client, "2026-09")).ShouldBe(new SummaryDto("2026-09", 0, 0, 0, 0));
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

        (await Summary(other, "2026-10")).ShouldBe(new SummaryDto("2026-10", 0, 0, 0, 0));
        (await Summary(e.Client, "2026-10")).IncomeCents.ShouldBe(800000);
    }
}
