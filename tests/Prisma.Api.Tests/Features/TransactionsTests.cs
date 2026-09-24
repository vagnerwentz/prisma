using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

[Collection(ApiCollection.Name)]
public sealed class TransactionsTests(PostgresFixture postgres)
{
    private sealed record TransactionDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate,
        Guid? StatementId, Guid? CategoryId, string Method, string Description, string Source);

    private sealed record IdDto(Guid Id);

    private sealed record SubDto(Guid Id, string Name);

    private sealed record NodeDto(Guid Id, string Name, string Type, List<SubDto> Subcategories);

    // Usuário logado com uma conta corrente e acesso às categorias padrão.
    private sealed class User(HttpClient client, Guid checkingId, List<NodeDto> categories) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid CheckingId { get; } = checkingId;

        public Guid Category(string type, string name) =>
            categories.Single(c => c.Type == type && c.Name == name).Id;

        public Guid Subcategory(string parent, string name) =>
            categories.Single(c => c.Type == "Expense" && c.Name == parent).Subcategories.Single(s => s.Name == name).Id;

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory Factory, User User)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        return (factory, await NewUser(factory));
    }

    private static async Task<User> NewUser(PrismaApiFactory factory)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await (await client.PostAsJsonAsync("/accounts",
            new { name = "Itaú", type = "Checking", initialBalanceCents = 0 })).Content.ReadFromJsonAsync<IdDto>();
        var categories = (await client.GetFromJsonAsync<List<NodeDto>>("/categories"))!;
        return new User(client, account!.Id, categories);
    }

    private static object Body(
        Guid accountId, string type = "Expense", long amountCents = 4590, string date = "2026-03-10",
        Guid? categoryId = null, string method = "Pix", string? description = "Padaria") =>
        new { accountId, type, amountCents, purchaseDate = date, categoryId, method, description };

    private static async Task<TransactionDto> Create(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/transactions", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        // POST /transactions sempre devolve uma lista; lançamento simples cria uma transação só.
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.ShouldHaveSingleItem();
    }

    private static async Task<List<TransactionDto>> List(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<List<TransactionDto>>($"/transactions{query}"))!;

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    [Fact]
    public async Task Creates_an_expense_that_settles_on_the_purchase_date()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var bakery = user.Subcategory("Alimentação", "Padaria");

        var created = await Create(user.Client, Body(user.CheckingId, categoryId: bakery));

        created.Type.ShouldBe("Expense");
        created.AmountCents.ShouldBe(4590);
        created.PurchaseDate.ShouldBe(new DateOnly(2026, 3, 10));
        created.SettlementDate.ShouldBe(new DateOnly(2026, 3, 10));
        created.CategoryId.ShouldBe(bakery);
        created.Source.ShouldBe("Manual");
        created.StatementId.ShouldBeNull();
        (await user.Client.GetFromJsonAsync<TransactionDto>($"/transactions/{created.Id}")).ShouldBe(created);
        (await List(user.Client)).ShouldBe([created]);
    }

    [Fact]
    public async Task Creates_an_income()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;

        var created = await Create(user.Client, Body(user.CheckingId, type: "Income", amountCents: 850000,
            categoryId: user.Category("Income", "Salário"), method: "Ted", description: "Salário de março"));

        created.Type.ShouldBe("Income");
        created.SettlementDate.ShouldBe(created.PurchaseDate);
    }

    [Fact]
    public async Task Invalid_transactions_are_rejected_in_portuguese()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var card = await (await user.Client.PostAsJsonAsync("/accounts", new
        {
            name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12,
        })).Content.ReadFromJsonAsync<IdDto>();

        async Task Rejects(object body, string detail) =>
            await ShouldBeProblem(await user.Client.PostAsJsonAsync("/transactions", body), HttpStatusCode.BadRequest, detail);

        await Rejects(Body(user.CheckingId, amountCents: 0), "O valor deve ser maior que zero.");
        await Rejects(Body(user.CheckingId, categoryId: user.Category("Income", "Salário")),
            "A categoria deve ser do mesmo tipo da transação (receita ou despesa).");
        // Em conta de cartão o lançamento vira compra no cartão (1.9a), que exige o crédito.
        await Rejects(Body(card!.Id), "Compra no cartão usa o meio de pagamento crédito.");
        await Rejects(Body(user.CheckingId, method: "Credit"), "Pagamento no crédito exige uma conta de cartão de crédito.");
        await Rejects(Body(user.CheckingId, type: "Transfer"), "Transferência entre contas usa a operação de transferência.");
        await Rejects(Body(Guid.NewGuid()), "Conta não encontrada.");
        await Rejects(Body(user.CheckingId, categoryId: Guid.NewGuid()), "Categoria não encontrada.");

        (await List(user.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Updates_a_transaction_and_settlement_follows_the_purchase_date()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var created = await Create(user.Client, Body(user.CheckingId));

        var response = await user.Client.PatchAsJsonAsync($"/transactions/{created.Id}", Body(user.CheckingId,
            amountCents: 5200, date: "2026-03-12", categoryId: user.Subcategory("Alimentação", "Mercado"),
            method: "Debit", description: "Mercado do bairro"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TransactionDto>();
        updated!.AmountCents.ShouldBe(5200);
        updated.PurchaseDate.ShouldBe(new DateOnly(2026, 3, 12));
        updated.SettlementDate.ShouldBe(new DateOnly(2026, 3, 12));
        updated.Method.ShouldBe("Debit");
        updated.Description.ShouldBe("Mercado do bairro");
    }

    [Fact]
    public async Task Soft_deleted_transaction_leaves_the_list_and_can_be_restored()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var created = await Create(user.Client, Body(user.CheckingId));

        (await user.Client.DeleteAsync($"/transactions/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(user.Client)).ShouldBeEmpty();
        (await user.Client.GetAsync($"/transactions/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var restored = await user.Client.PostAsync($"/transactions/{created.Id}/restore", null);

        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await List(user.Client)).ShouldBe([created]);

        // Restaurar algo que não está excluído é 404.
        await ShouldBeProblem(await user.Client.PostAsync($"/transactions/{created.Id}/restore", null),
            HttpStatusCode.NotFound, "Transação excluída não encontrada.");
    }

    [Fact]
    public async Task Restore_clears_a_category_deleted_meanwhile()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var cinema = user.Subcategory("Lazer", "Cinema");
        var created = await Create(user.Client, Body(user.CheckingId, categoryId: cinema));
        await user.Client.DeleteAsync($"/transactions/{created.Id}");
        (await user.Client.DeleteAsync($"/categories/{cinema}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var restored = await (await user.Client.PostAsync($"/transactions/{created.Id}/restore", null))
            .Content.ReadFromJsonAsync<TransactionDto>();

        restored!.CategoryId.ShouldBeNull();
    }

    [Fact]
    public async Task Restore_is_refused_when_the_account_was_deleted()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var created = await Create(user.Client, Body(user.CheckingId));
        await user.Client.DeleteAsync($"/transactions/{created.Id}");
        (await user.Client.DeleteAsync($"/accounts/{user.CheckingId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeProblem(await user.Client.PostAsync($"/transactions/{created.Id}/restore", null),
            HttpStatusCode.Conflict, "A conta desta transação foi excluída; não é possível restaurá-la.");
    }

    [Fact]
    public async Task Category_with_transactions_cannot_be_deleted()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var market = user.Subcategory("Alimentação", "Mercado");
        var created = await Create(user.Client, Body(user.CheckingId, categoryId: market));

        await ShouldBeProblem(await user.Client.DeleteAsync($"/categories/{market}"), HttpStatusCode.Conflict,
            "Esta categoria tem transações. Mova as transações para outra categoria antes de excluir.");

        // Com a transação excluída, a categoria pode sair.
        await user.Client.DeleteAsync($"/transactions/{created.Id}");
        (await user.Client.DeleteAsync($"/categories/{market}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Account_with_transactions_cannot_be_deleted()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        await Create(user.Client, Body(user.CheckingId));

        await ShouldBeProblem(await user.Client.DeleteAsync($"/accounts/{user.CheckingId}"), HttpStatusCode.Conflict,
            "Esta conta tem transações. Mova ou exclua as transações antes, ou marque a conta como inativa.");
    }

    [Fact]
    public async Task Filters_by_purchase_period_account_category_and_search()
    {
        var (factory, user) = await Start();
        await using var _ = factory;
        using var __ = user;
        var savings = await (await user.Client.PostAsJsonAsync("/accounts",
            new { name = "Carteira", type = "Cash", initialBalanceCents = 0 })).Content.ReadFromJsonAsync<IdDto>();

        var bakery = await Create(user.Client, Body(user.CheckingId, date: "2026-03-01",
            categoryId: user.Subcategory("Alimentação", "Padaria"), description: "Pão de Queijo"));
        var market = await Create(user.Client, Body(user.CheckingId, date: "2026-03-15",
            categoryId: user.Subcategory("Alimentação", "Mercado"), description: "Mercado 100%"));
        var fuel = await Create(user.Client, Body(savings!.Id, date: "2026-03-31", method: "Cash",
            categoryId: user.Subcategory("Transporte", "Combustível"), description: "Posto"));
        var april = await Create(user.Client, Body(user.CheckingId, date: "2026-04-01", description: "Abril"));

        // Mais recentes primeiro.
        (await List(user.Client)).Select(t => t.Id).ShouldBe([april.Id, fuel.Id, market.Id, bakery.Id]);

        (await List(user.Client, "?from=2026-03-01&to=2026-03-31")).Select(t => t.Id)
            .ShouldBe([fuel.Id, market.Id, bakery.Id]);
        (await List(user.Client, $"?accountId={savings.Id}")).Select(t => t.Id).ShouldBe([fuel.Id]);

        // Filtrar pela categoria inclui as subcategorias.
        (await List(user.Client, $"?categoryId={user.Category("Expense", "Alimentação")}")).Select(t => t.Id)
            .ShouldBe([market.Id, bakery.Id]);

        // Busca sem diferenciar maiúsculas. "%" e "_" são texto, não curingas do LIKE:
        // sem o escape, buscar "%" traria todas as transações.
        (await List(user.Client, "?search=queijo")).Select(t => t.Id).ShouldBe([bakery.Id]);
        (await List(user.Client, "?search=%25")).Select(t => t.Id).ShouldBe([market.Id]);
        (await List(user.Client, "?search=_")).ShouldBeEmpty();

        await ShouldBeProblem(await user.Client.GetAsync("/transactions?from=2026-04-01&to=2026-03-01"),
            HttpStatusCode.BadRequest, "A data inicial deve ser anterior ou igual à data final.");
    }

    [Fact]
    public async Task Transactions_of_another_user_are_invisible()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await NewUser(factory);
        using var intruder = await NewUser(factory);
        var created = await Create(owner.Client, Body(owner.CheckingId));

        (await List(intruder.Client)).ShouldBeEmpty();
        (await intruder.Client.GetAsync($"/transactions/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PatchAsJsonAsync($"/transactions/{created.Id}", Body(intruder.CheckingId)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.DeleteAsync($"/transactions/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nem a conta do outro usuário pode ser usada num lançamento.
        await ShouldBeProblem(await intruder.Client.PostAsJsonAsync("/transactions", Body(owner.CheckingId)),
            HttpStatusCode.BadRequest, "Conta não encontrada.");

        // Restaurar ignora só o soft delete: a transação excluída do outro continua invisível.
        await owner.Client.DeleteAsync($"/transactions/{created.Id}");
        (await intruder.Client.PostAsync($"/transactions/{created.Id}/restore", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = factory.CreateHttpsClient();

        (await client.GetAsync("/transactions")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
