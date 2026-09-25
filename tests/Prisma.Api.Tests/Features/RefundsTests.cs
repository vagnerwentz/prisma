using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 2.5 (docs/fase-2.md, 2.5), com o exemplo da regra. Hoje é 15/10/2026; Visa e Master fecham
// dia 26 e vencem dia 5.
[Collection(ApiCollection.Name)]
public sealed class RefundsTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record NodeDto(Guid Id, string Name, List<NodeDto>? Subcategories);

    private sealed record EntryDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly SettlementDate, Guid? StatementId, Guid? CategoryId,
        Guid? InstallmentPurchaseId, Guid? RefundedTransactionId, long? RefundedCents, long? RefundableCents);

    private sealed record StatementDto(Guid Id, string Reference, bool IsPaid, long TotalCents);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents, long? OwedCents, long? AvailableCreditCents);

    private sealed record SummaryDto(string Month, long IncomeCents, long ExpenseCents, long CardExpenseCents);

    private sealed record CategoryDto(Guid? CategoryId, string? Name, long AmountCents);

    private sealed record CategoriesDto(List<CategoryDto> Categories, long HiddenRefundCents);

    private sealed record UpcomingDto(long TotalCents, List<IdDto> Statements);

    private static readonly FakeClock October15 = new(new DateTime(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc));

    private sealed class Example(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Itau, Visa, Master, Food, Restaurant, Grocery, Shopping;
        public EntryDto Dinner = null!, GroceryRun = null!;

        public static async Task<Example> Create(PrismaApiFactory factory)
        {
            var e = new Example(await factory.CreateAuthenticatedClientAsync());
            e.Itau = await e.Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            e.Visa = await e.Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5, creditLimitCents = 500000 });
            e.Master = await e.Account(new { name = "Master", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5, creditLimitCents = 100000 });

            var tree = (await e.Client.GetFromJsonAsync<List<NodeDto>>("/categories"))!;
            e.Food = tree.Single(c => c.Name == "Alimentação").Id;
            e.Restaurant = tree.Single(c => c.Name == "Alimentação").Subcategories!.Single(c => c.Name == "Restaurante").Id;
            e.Grocery = tree.Single(c => c.Name == "Alimentação").Subcategories!.Single(c => c.Name == "Mercado").Id;
            e.Shopping = tree.Single(c => c.Name == "Compras").Id;

            e.Dinner = await e.Purchase(e.Visa, 60000, "2026-09-20", e.Restaurant);                          // 1
            (await e.Client.PostAsJsonAsync($"/statements/{e.Dinner.StatementId}/pay",
                new { fromAccountId = e.Itau, date = "2026-10-05", method = "Boleto" })).StatusCode.ShouldBe(HttpStatusCode.Created); // 2
            await e.Refunded(e.Visa, 15000, "2026-10-10", e.Restaurant, e.Dinner.Id);                        // 3
            await e.Purchase(e.Visa, 30000, "2026-10-10", e.Shopping);                                       // 4
            await e.Refunded(e.Visa, 4000, "2026-10-12", e.Shopping);                                        // 5
            e.GroceryRun = await e.Purchase(e.Itau, 25000, "2026-10-03", e.Grocery, "Debit");               // 6
            await e.Refunded(e.Itau, 5000, "2026-10-06", e.Grocery, e.GroceryRun.Id, "Debit");              // 7
            await e.Purchase(e.Master, 20000, "2026-10-12", e.Shopping);                                     // 8
            await e.Refunded(e.Master, 30000, "2026-10-14", e.Shopping);                                     // 9
            return e;
        }

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public async Task<EntryDto> Purchase(Guid account, long cents, string date, Guid? category, string? method = null, int installments = 1)
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = account, type = "Expense", amountCents = cents, purchaseDate = date,
                method = method ?? (account == Itau ? "Pix" : "Credit"), categoryId = category, installments,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<List<EntryDto>>())![0];
        }

        public Task<HttpResponseMessage> Refund(Guid account, long cents, string date, Guid? category = null, Guid? refunded = null, string? method = null) =>
            Client.PostAsJsonAsync("/transactions", new
            {
                accountId = account, type = "Refund", amountCents = cents, purchaseDate = date,
                method = method ?? (account == Itau ? "Pix" : "Credit"), categoryId = category, description = "Estorno",
                refundedTransactionId = refunded,
            });

        public async Task<EntryDto> Refunded(Guid account, long cents, string date, Guid? category = null, Guid? refunded = null, string? method = null)
        {
            var response = await Refund(account, cents, date, category, refunded, method);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<List<EntryDto>>())!.Single();
        }

        public async Task<List<StatementDto>> Statements(Guid card) =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{card}/statements"))!.OrderBy(s => s.Reference).ToList();

        public async Task<List<EntryDto>> List(string query = "") =>
            (await Client.GetFromJsonAsync<List<EntryDto>>($"/transactions{query}"))!;

        public void Dispose() => Client.Dispose();
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    [Fact]
    public async Task Spec_example_statements_balances_and_upcoming()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        (await e.Statements(e.Visa)).Select(s => (s.Reference, s.IsPaid, s.TotalCents))
            .ShouldBe([("2026-10", true, 60000L), ("2026-11", false, 11000L)]);
        var masterNovember = (await e.Statements(e.Master)).Single();
        masterNovember.TotalCents.ShouldBe(-10000); // saldo a favor
        await ShouldBeProblem(await e.Client.PostAsJsonAsync($"/statements/{masterNovember.Id}/pay",
            new { fromAccountId = e.Itau, date = "2026-10-27", method = "Boleto" }), HttpStatusCode.BadRequest, "Não há valor a pagar nesta fatura.");

        var upcoming = (await e.Client.GetFromJsonAsync<UpcomingDto>("/dashboard/upcoming-statements"))!;
        upcoming.TotalCents.ShouldBe(11000);
        upcoming.Statements.Count.ShouldBe(1);

        var balances = (await e.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!.ToDictionary(b => b.AccountId);
        (balances[e.Visa].OwedCents, balances[e.Visa].AvailableCreditCents).ShouldBe((11000L, 489000L));
        (balances[e.Master].OwedCents, balances[e.Master].AvailableCreditCents).ShouldBe((0L, 100000L));
        balances[e.Itau].BalanceCents.ShouldBe(-80000);
    }

    [Fact]
    public async Task Spec_example_summary_and_categories()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        async Task<SummaryDto> Summary(string month) => (await e.Client.GetFromJsonAsync<SummaryDto>($"/dashboard/summary?month={month}"))!;
        async Task<CategoriesDto> Categories(string month) => (await e.Client.GetFromJsonAsync<CategoriesDto>($"/dashboard/categories?month={month}"))!;

        (await Summary("2026-10")).ShouldBe(new SummaryDto("2026-10", 0, 80000, 60000));
        (await Summary("2026-11")).ShouldBe(new SummaryDto("2026-11", 0, 1000, 1000));

        var october = await Categories("2026-10");
        october.Categories.Select(c => (c.CategoryId, c.AmountCents)).ShouldBe([(e.Food, 80000L)]);
        october.HiddenRefundCents.ShouldBe(0);

        var november = await Categories("2026-11");
        november.Categories.Select(c => (c.CategoryId, c.AmountCents)).ShouldBe([(e.Shopping, 16000L)]);
        november.HiddenRefundCents.ShouldBe(15000);

        // Detalhamento: despesas e estornos da categoria pela data de caixa somam o valor exibido.
        var shopping = await e.List($"?from=2026-11-01&to=2026-11-30&dateBasis=Settlement&type=Expense&type=Refund&categoryId={e.Shopping}");
        shopping.Sum(t => t.Type == "Refund" ? -t.AmountCents : t.AmountCents).ShouldBe(16000);
    }

    [Fact]
    public async Task Linked_refunds_are_limited_to_the_purchase_and_shown_on_it()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        await ShouldBeProblem(await e.Refund(e.Visa, 50000, "2026-10-12", refunded: e.Dinner.Id), HttpStatusCode.BadRequest,
            "O estorno passa do valor da compra. Restam R$ 450,00 para estornar.");
        var dinner = (await e.List()).Single(t => t.Id == e.Dinner.Id);
        (dinner.RefundedCents, dinner.RefundableCents).ShouldBe((15000L, 45000L));

        await e.Refunded(e.Visa, 45000, "2026-10-12", refunded: e.Dinner.Id);
        dinner = (await e.List()).Single(t => t.Id == e.Dinner.Id);
        (dinner.RefundedCents, dinner.RefundableCents).ShouldBe((60000L, 0L));
        await ShouldBeProblem(await e.Refund(e.Visa, 1, "2026-10-12", refunded: e.Dinner.Id), HttpStatusCode.BadRequest,
            "Esta compra já foi estornada por inteiro.");

        // Na outra conta, não; e fatura paga não recebe estorno.
        await ShouldBeProblem(await e.Refund(e.Master, 100, "2026-10-12", refunded: e.GroceryRun.Id), HttpStatusCode.BadRequest,
            "O estorno fica na mesma conta da compra.");
        await ShouldBeProblem(await e.Refund(e.Visa, 100, "2026-09-25"), HttpStatusCode.BadRequest,
            "Esta fatura já está paga. Lance o estorno com a data em que ele apareceu no cartão.");
    }

    [Fact]
    public async Task An_installment_purchase_is_refunded_with_a_single_credit()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        var bike = await e.Purchase(e.Master, 90000, "2026-10-12", e.Shopping, installments: 3);

        var refund = await e.Refunded(e.Master, 90000, "2026-10-14", e.Shopping, bike.Id);

        refund.SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
        var installments = (await e.List()).Where(t => t.InstallmentPurchaseId == bike.InstallmentPurchaseId).ToList();
        installments.Count.ShouldBe(3);
        installments.ShouldAllBe(t => t.RefundedCents == 90000 && t.RefundableCents == 0);
        await ShouldBeProblem(await e.Refund(e.Master, 1, "2026-10-14", refunded: installments.Last().Id), HttpStatusCode.BadRequest,
            "Esta compra já foi estornada por inteiro.");
        await ShouldBeProblem(await e.Client.PatchAsJsonAsync($"/installment-purchases/{bike.InstallmentPurchaseId}",
                new { totalAmountCents = 80000, installmentCount = 3, description = "Bike", purchaseDate = "2026-10-12" }),
            HttpStatusCode.BadRequest, "Já foram estornados R$ 900,00 desta compra; o valor não pode ficar abaixo disso.");
    }

    [Fact]
    public async Task Editing_a_refund_moves_it_between_statements_and_keeps_the_limit()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        var refund = (await e.List()).Single(t => t.RefundedTransactionId == e.Dinner.Id);

        Task<HttpResponseMessage> Patch(long cents, string date) => e.Client.PatchAsJsonAsync($"/transactions/{refund.Id}", new
        {
            accountId = e.Visa, type = "Refund", amountCents = cents, purchaseDate = date, method = "Credit",
            categoryId = e.Restaurant, description = "Estorno",
        });

        var moved = await Patch(15000, "2026-10-30");
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        (await moved.Content.ReadFromJsonAsync<EntryDto>())!.SettlementDate.ShouldBe(new DateOnly(2026, 12, 5));
        (await e.Statements(e.Visa)).Select(s => (s.Reference, s.TotalCents))
            .ShouldBe([("2026-10", 60000L), ("2026-11", 26000L), ("2026-12", -15000L)]);

        await ShouldBeProblem(await Patch(60001, "2026-10-30"), HttpStatusCode.BadRequest,
            "O estorno passa do valor da compra. Restam R$ 600,00 para estornar.");
        await ShouldBeProblem(await Patch(15000, "2026-09-25"), HttpStatusCode.BadRequest,
            "A nova data leva o estorno para uma fatura já paga.");
    }

    [Fact]
    public async Task Deleting_and_restoring_a_refund_follow_the_rules()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        var first = (await e.List()).Single(t => t.RefundedTransactionId == e.Dinner.Id);

        (await e.Client.DeleteAsync($"/transactions/{first.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await e.Statements(e.Visa)).Single(s => s.Reference == "2026-11").TotalCents.ShouldBe(26000);

        // Outro estorno usa todo o valor da compra; restaurar o primeiro passaria do limite.
        await e.Refunded(e.Visa, 60000, "2026-10-12", refunded: e.Dinner.Id);
        await ShouldBeProblem(await e.Client.PostAsync($"/transactions/{first.Id}/restore", null), HttpStatusCode.BadRequest,
            "Esta compra já foi estornada por inteiro.");

        // Excluir a compra original não exclui os estornos dela.
        (await e.Client.DeleteAsync($"/transactions/{e.GroceryRun.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await e.List()).ShouldContain(t => t.RefundedTransactionId == e.GroceryRun.Id);
    }

    [Fact]
    public async Task A_refunded_expense_keeps_type_account_and_a_minimum_amount()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);

        Task<HttpResponseMessage> Patch(string type, long cents, Guid? category) => e.Client.PatchAsJsonAsync($"/transactions/{e.GroceryRun.Id}", new
        {
            accountId = e.Itau, type, amountCents = cents, purchaseDate = "2026-10-03", method = "Debit", categoryId = category,
        });

        await ShouldBeProblem(await Patch("Expense", 4000, e.Grocery), HttpStatusCode.BadRequest,
            "Já foram estornados R$ 50,00 desta compra; o valor não pode ficar abaixo disso.");
        await ShouldBeProblem(await Patch("Income", 25000, null), HttpStatusCode.BadRequest,
            "Esta despesa tem estornos; o tipo e a conta não mudam.");
        (await Patch("Expense", 5000, e.Grocery)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refund_rules_on_accounts_and_isolation()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, clock: October15);
        using var e = await Example.Create(factory);
        var treasury = (await (await e.Client.PostAsJsonAsync("/accounts", new { name = "Tesouro", type = "Investment", initialBalanceCents = 0 }))
            .Content.ReadFromJsonAsync<IdDto>())!.Id;

        await ShouldBeProblem(await e.Refund(treasury, 100, "2026-10-12", method: "Pix"), HttpStatusCode.BadRequest,
            "Estorno vai para a conta ou o cartão em que a compra foi feita.");
        await ShouldBeProblem(await e.Client.PostAsJsonAsync("/transactions", new
            {
                accountId = e.Visa, type = "Refund", amountCents = 100, purchaseDate = "2026-10-12", method = "Credit", installments = 2,
            }), HttpStatusCode.BadRequest, "Estorno não tem parcelas.");
        await ShouldBeProblem(await e.Client.PostAsJsonAsync("/transactions", new
            {
                accountId = e.Itau, type = "Expense", amountCents = 100, purchaseDate = "2026-10-12", method = "Pix", refundedTransactionId = e.Dinner.Id,
            }), HttpStatusCode.BadRequest, "Só o estorno aponta a compra estornada.");

        using var other = new Example(await factory.CreateAuthenticatedClientAsync());
        var otherCard = (await (await other.Client.PostAsJsonAsync("/accounts",
                new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 }))
            .Content.ReadFromJsonAsync<IdDto>())!.Id;
        await ShouldBeProblem(await other.Client.PostAsJsonAsync("/transactions", new
            {
                accountId = otherCard, type = "Refund", amountCents = 100, purchaseDate = "2026-10-12", method = "Credit",
                refundedTransactionId = e.Dinner.Id,
            }), HttpStatusCode.BadRequest, "Compra estornada não encontrada.");
        (await other.List()).ShouldBeEmpty();
    }
}
