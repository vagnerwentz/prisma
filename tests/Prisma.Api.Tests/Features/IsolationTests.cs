using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 1.11 (docs/fase-1.md, seção 3): o dono (A) cadastra um pouco de tudo e o intruso (B) tenta
// chegar a esses dados por todos os endpoints. Um id de A precisa se comportar, para B, exatamente
// como um id que não existe: mesma resposta, e 404 quando o id está na rota (403 confirmaria que existe).
[Collection(ApiCollection.Name)]
public sealed class IsolationTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id, Guid? StatementId, Guid? InstallmentPurchaseId);

    private sealed record CategoryNode(Guid Id, List<IdDto> Subcategories);

    // Dados de um usuário, criados pela API como a tela faria.
    private sealed class User(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid Cash { get; private set; }
        public Guid Card { get; private set; }
        public Guid Category { get; private set; }
        public Guid Subcategory { get; private set; }
        public Guid Transaction { get; private set; }
        public Guid DeletedTransaction { get; private set; }
        public Guid Purchase { get; private set; }
        public Guid DeletedPurchase { get; private set; }
        public Guid Statement { get; private set; }
        public Guid TransferLeg { get; private set; }

        public static async Task<User> Create(PrismaApiFactory factory)
        {
            var u = new User(await factory.CreateAuthenticatedClientAsync());
            u.Checking = await u.PostId("/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            u.Cash = await u.PostId("/accounts", new { name = "Carteira", type = "Cash", initialBalanceCents = 0 });
            u.Card = await u.PostId("/accounts",
                new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
            u.Category = await u.PostId("/categories", new { name = "Pets", type = "Expense" });
            u.Subcategory = await u.PostId("/categories", new { name = "Ração", type = "Expense", parentCategoryId = u.Category });

            u.Transaction = (await u.Post<List<IdDto>>("/transactions", u.Simple(u.Checking, u.Category)))[0].Id;
            u.DeletedTransaction = (await u.Post<List<IdDto>>("/transactions", u.Simple(u.Checking, u.Category)))[0].Id;
            (await u.Client.DeleteAsync($"/transactions/{u.DeletedTransaction}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var installments = await u.Post<List<TransactionDto>>("/transactions", CardPurchase(u.Card, 3));
            u.Purchase = installments[0].InstallmentPurchaseId!.Value;
            u.Statement = installments[0].StatementId!.Value;
            u.DeletedPurchase = (await u.Post<List<TransactionDto>>("/transactions", CardPurchase(u.Card, 2)))[0].InstallmentPurchaseId!.Value;
            (await u.Client.DeleteAsync($"/installment-purchases/{u.DeletedPurchase}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            u.TransferLeg = (await u.Post<List<IdDto>>("/transfers",
                new { fromAccountId = u.Checking, toAccountId = u.Cash, amountCents = 5000, date = "2026-03-15", method = "Pix" }))[0].Id;
            return u;
        }

        public object Simple(Guid account, Guid? category) =>
            new { accountId = account, type = "Expense", amountCents = 4590, purchaseDate = "2026-03-10", categoryId = category, method = "Pix", description = "Petz" };

        // Em 10/03/2026 num cartão que fecha dia 5: a 1ª parcela cai na fatura de abril (fecha 05/04).
        private static object CardPurchase(Guid card, int installments) =>
            new { accountId = card, type = "Expense", amountCents = 30000, purchaseDate = "2026-03-10", method = "Credit", description = "Notebook", installments };

        public async Task<T> Post<T>(string url, object body)
        {
            var response = await Client.PostAsJsonAsync(url, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        public async Task<Guid> PostId(string url, object body) => (await Post<IdDto>(url, body)).Id;

        public void Dispose() => Client.Dispose();
    }

    // O mesmo pedido com o id de A e com um id que não existe: as respostas precisam ser iguais.
    private static async Task<HttpResponseMessage> ShouldLookNonexistent(Guid ownersId, Func<Guid, Task<HttpResponseMessage>> probe)
    {
        var withOwnersId = await probe(ownersId);
        var withRandomId = await probe(Guid.NewGuid());

        withOwnersId.StatusCode.ShouldBe(withRandomId.StatusCode);
        withOwnersId.IsSuccessStatusCode.ShouldBeFalse();
        (await DetailOf(withOwnersId)).ShouldBe(await DetailOf(withRandomId));
        return withOwnersId;
    }

    // Id na rota: além de igual ao inexistente, tem de ser 404.
    private static async Task ShouldBeNotFound(Guid ownersId, Func<Guid, Task<HttpResponseMessage>> probe) =>
        (await ShouldLookNonexistent(ownersId, probe)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

    private static async Task<string?> DetailOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length == 0) return null;
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        return $"{problem.Title} | {problem.Detail}";
    }

    // Retrato do que A enxerga; tem de sair igual depois das tentativas de B.
    private static async Task<string> Snapshot(User u)
    {
        var parts = new List<string>();
        foreach (var url in new[] { "/accounts", "/categories", "/transactions", $"/accounts/{u.Card}/statements" })
            parts.Add(await u.Client.GetStringAsync(url));
        return string.Join("\n", parts);
    }

    [Fact]
    public async Task Listings_never_show_the_other_users_data()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await User.Create(factory);
        using var intruder = await User.Create(factory);
        HttpClient b = intruder.Client;

        var accounts = await b.GetFromJsonAsync<List<IdDto>>("/accounts");
        accounts!.Select(a => a.Id).ShouldBe([intruder.Checking, intruder.Cash, intruder.Card], ignoreOrder: true);

        var categories = (await b.GetFromJsonAsync<List<CategoryNode>>("/categories"))!
            .SelectMany(c => c.Subcategories.Select(s => s.Id).Prepend(c.Id)).ToList();
        categories.ShouldContain(intruder.Category);
        categories.ShouldNotContain(owner.Category);
        categories.ShouldNotContain(owner.Subcategory);

        var ownersTransactions = (await owner.Client.GetFromJsonAsync<List<IdDto>>("/transactions"))!.Select(t => t.Id);
        var seenByIntruder = (await b.GetFromJsonAsync<List<IdDto>>("/transactions"))!.Select(t => t.Id);
        seenByIntruder.ShouldNotBeEmpty();
        seenByIntruder.Intersect(ownersTransactions).ShouldBeEmpty();

        // Filtrar pela conta, fatura ou categoria de A não abre uma brecha: vem vazio.
        foreach (var query in new[] { $"accountId={owner.Checking}", $"accountId={owner.Card}", $"statementId={owner.Statement}", $"categoryId={owner.Category}" })
            (await b.GetFromJsonAsync<List<IdDto>>($"/transactions?{query}"))!.ShouldBeEmpty(query);
    }

    [Fact]
    public async Task Reading_or_changing_by_id_returns_404()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await User.Create(factory);
        using var intruder = await User.Create(factory);
        HttpClient b = intruder.Client;
        var before = await Snapshot(owner);

        // Contas
        await ShouldBeNotFound(owner.Checking, id => b.PatchAsJsonAsync($"/accounts/{id}",
            new { name = "Invadida", initialBalanceCents = 0, isActive = false }));
        await ShouldBeNotFound(owner.Checking, id => b.DeleteAsync($"/accounts/{id}"));
        await ShouldBeNotFound(owner.Card, id => b.GetAsync($"/accounts/{id}/statements"));

        // Categorias
        await ShouldBeNotFound(owner.Category, id => b.PatchAsJsonAsync($"/categories/{id}", new { name = "Invadida" }));
        await ShouldBeNotFound(owner.Subcategory, id => b.DeleteAsync($"/categories/{id}"));

        // Lançamentos, inclusive a restauração, que ignora só o filtro de exclusão
        await ShouldBeNotFound(owner.Transaction, id => b.GetAsync($"/transactions/{id}"));
        await ShouldBeNotFound(owner.Transaction, id => b.PatchAsJsonAsync($"/transactions/{id}", intruder.Simple(intruder.Checking, null)));
        await ShouldBeNotFound(owner.Transaction, id => b.DeleteAsync($"/transactions/{id}"));
        await ShouldBeNotFound(owner.DeletedTransaction, id => b.PostAsync($"/transactions/{id}/restore", null));
        await ShouldBeNotFound(owner.TransferLeg, id => b.GetAsync($"/transactions/{id}"));
        await ShouldBeNotFound(owner.TransferLeg, id => b.DeleteAsync($"/transactions/{id}"));

        // Compras parceladas
        await ShouldBeNotFound(owner.Purchase, id => b.PatchAsJsonAsync($"/installment-purchases/{id}",
            new { totalAmountCents = 100, installmentCount = 1, description = "Invadida", purchaseDate = "2026-03-10" }));
        await ShouldBeNotFound(owner.Purchase, id => b.DeleteAsync($"/installment-purchases/{id}"));
        await ShouldBeNotFound(owner.DeletedPurchase, id => b.PostAsync($"/installment-purchases/{id}/restore", null));

        // Faturas
        await ShouldBeNotFound(owner.Statement, id => b.PatchAsJsonAsync($"/statements/{id}",
            new { closingDate = "2026-04-06", dueDate = "2026-04-13" }));
        await ShouldBeNotFound(owner.Statement, id => b.PostAsJsonAsync($"/statements/{id}/pay",
            new { fromAccountId = intruder.Checking, date = "2026-04-10", method = "Boleto" }));

        (await Snapshot(owner)).ShouldBe(before);
    }

    [Fact]
    public async Task Referencing_the_other_users_ids_looks_like_a_missing_id()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await User.Create(factory);
        using var intruder = await User.Create(factory);
        HttpClient b = intruder.Client;
        var before = await Snapshot(owner);

        // Lançar na conta ou na categoria de A
        await ShouldLookNonexistent(owner.Checking, id => b.PostAsJsonAsync("/transactions", intruder.Simple(id, null)));
        await ShouldLookNonexistent(owner.Card, id => b.PostAsJsonAsync("/transactions",
            new { accountId = id, type = "Expense", amountCents = 1000, purchaseDate = "2026-03-10", method = "Credit", installments = 2 }));
        await ShouldLookNonexistent(owner.Category, id => b.PostAsJsonAsync("/transactions", intruder.Simple(intruder.Checking, id)));

        // Mover um lançamento de B para a conta ou a categoria de A
        await ShouldLookNonexistent(owner.Checking, id => b.PatchAsJsonAsync($"/transactions/{intruder.Transaction}", intruder.Simple(id, null)));
        await ShouldLookNonexistent(owner.Category, id =>
            b.PatchAsJsonAsync($"/transactions/{intruder.Transaction}", intruder.Simple(intruder.Checking, id)));
        await ShouldLookNonexistent(owner.Category, id => b.PatchAsJsonAsync($"/installment-purchases/{intruder.Purchase}",
            new { totalAmountCents = 30000, installmentCount = 3, categoryId = id, description = "Notebook", purchaseDate = "2026-03-10" }));

        // Subcategoria dentro da categoria de A
        await ShouldLookNonexistent(owner.Category, id => b.PostAsJsonAsync("/categories",
            new { name = "Intrusa", type = "Expense", parentCategoryId = id }));

        // Transferir de ou para a conta de A, e pagar a fatura de B com dinheiro de A
        await ShouldLookNonexistent(owner.Checking, id => b.PostAsJsonAsync("/transfers",
            new { fromAccountId = intruder.Checking, toAccountId = id, amountCents = 1000, date = "2026-03-15", method = "Pix" }));
        await ShouldLookNonexistent(owner.Checking, id => b.PostAsJsonAsync("/transfers",
            new { fromAccountId = id, toAccountId = intruder.Checking, amountCents = 1000, date = "2026-03-15", method = "Pix" }));
        await ShouldLookNonexistent(owner.Checking, id => b.PostAsJsonAsync($"/statements/{intruder.Statement}/pay",
            new { fromAccountId = id, date = "2026-04-10", method = "Boleto" }));

        (await Snapshot(owner)).ShouldBe(before);
    }
}
