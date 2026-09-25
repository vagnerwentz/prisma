using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 1.10 (docs/fase-1.md, 2.3). Cartão que fecha dia 5 e vence dia 12; compra de 10/03/2026
// em 3x cai nas faturas de abril (fecha 05/04), maio e junho.
[Collection(ApiCollection.Name)]
public sealed class TransfersTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate,
        Guid? StatementId, Guid? CategoryId, Guid? TransferPairId, string? TransferDirection, Guid? InstallmentPurchaseId);

    private sealed record StatementDto(Guid Id, string Reference, bool IsPaid, long TotalCents);

    private sealed class Scenario(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; set; }
        public Guid Cash { get; set; }
        public Guid Card { get; set; }

        public async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public Task<HttpResponseMessage> Transfer(Guid from, Guid to, long amount, string date = "2026-04-10", string method = "Pix") =>
            Client.PostAsJsonAsync("/transfers", new { fromAccountId = from, toAccountId = to, amountCents = amount, date, method });

        public async Task<List<TransactionDto>> Buy(long amount, int installments, string date = "2026-03-10")
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = Card, type = "Expense", amountCents = amount, purchaseDate = date, method = "Credit", description = "Notebook", installments,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        }

        public Task<HttpResponseMessage> Pay(Guid statementId, string date = "2026-04-10", Guid? from = null) =>
            Client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = from ?? Checking, date, method = "Boleto" });

        public async Task<List<StatementDto>> Statements() =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{Card}/statements"))!.OrderBy(s => s.Reference).ToList();

        public async Task<StatementDto> Statement(string reference) => (await Statements()).Single(s => s.Reference == reference);

        public async Task<List<TransactionDto>> Transactions(string query = "") =>
            (await Client.GetFromJsonAsync<List<TransactionDto>>($"/transactions{query}"))!;

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        var s = new Scenario(await factory.CreateAuthenticatedClientAsync());
        s.Checking = await s.Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
        s.Cash = await s.Account(new { name = "Carteira", type = "Cash", initialBalanceCents = 0 });
        s.Card = await s.Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
        return (factory, s);
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    // Receita e despesa de uma lista: o que o dashboard somaria. Transferência fica de fora.
    private static (long Income, long Expense) Totals(IEnumerable<TransactionDto> transactions) =>
        (transactions.Where(t => t.Type == "Income").Sum(t => t.AmountCents),
         transactions.Where(t => t.Type == "Expense").Sum(t => t.AmountCents));

    [Fact]
    public async Task Transfer_creates_two_linked_legs_outside_income_and_expense()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Client.PostAsJsonAsync("/transactions", new { accountId = s.Checking, type = "Income", amountCents = 800000, purchaseDate = "2026-04-05", method = "Pix" });
        var before = Totals(await s.Transactions());

        var response = await s.Transfer(s.Checking, s.Cash, 20000);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var legs = (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        legs.Count.ShouldBe(2);
        legs.Select(l => (l.AccountId, l.TransferDirection)).ShouldBe([(s.Checking, "Out"), (s.Cash, "In")]);
        legs.ShouldAllBe(l => l.Type == "Transfer" && l.TransferPairId == legs[0].TransferPairId && l.CategoryId == null);
        Totals(await s.Transactions()).ShouldBe(before);
        Totals(await s.Transactions("?from=2026-04-01&to=2026-04-30")).ShouldBe(before);
    }

    [Fact]
    public async Task Transfer_to_a_card_or_between_the_same_account_is_refused()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        await ShouldBeProblem(await s.Transfer(s.Checking, s.Card, 1000), HttpStatusCode.BadRequest,
            "O cartão de crédito recebe dinheiro só pelo pagamento da fatura.");
        await ShouldBeProblem(await s.Transfer(s.Checking, s.Checking, 1000), HttpStatusCode.BadRequest,
            "Escolha contas diferentes para a transferência.");
        (await s.Transactions()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_one_leg_deletes_both_and_restoring_brings_both_back()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var legs = (await (await s.Transfer(s.Checking, s.Cash, 20000)).Content.ReadFromJsonAsync<List<TransactionDto>>())!;

        (await s.Client.DeleteAsync($"/transactions/{legs[1].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Transactions()).ShouldBeEmpty();

        (await s.Client.PostAsync($"/transactions/{legs[0].Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Transactions()).Select(t => t.Id).ShouldBe(legs.Select(l => l.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task Transfer_legs_are_not_edited()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var legs = (await (await s.Transfer(s.Checking, s.Cash, 20000)).Content.ReadFromJsonAsync<List<TransactionDto>>())!;

        await ShouldBeProblem(await s.Client.PatchAsJsonAsync($"/transactions/{legs[0].Id}", new
            {
                accountId = s.Checking, type = "Expense", amountCents = 20000, purchaseDate = "2026-04-10", method = "Pix",
            }),
            HttpStatusCode.BadRequest, "Transferência não é editada. Exclua e lance de novo.");
    }

    // --- Pagamento de fatura ---

    [Fact]
    public async Task Paying_a_statement_does_not_duplicate_the_months_spending()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(30000, 3);
        var april = await s.Statement("2026-04");

        var response = await s.Pay(april.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var paid = await s.Statement("2026-04");
        paid.IsPaid.ShouldBeTrue();
        paid.TotalCents.ShouldBe(10000);
        var legs = (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        legs.Select(l => (l.AccountId, l.AmountCents)).ShouldBe([(s.Checking, 10000L), (s.Card, 10000L)]);
        legs[1].StatementId.ShouldBe(april.Id);
        // Caixa de abril: a parcela vence 12/04 e o pagamento sai em 10/04; gasto só a parcela.
        var aprilCash = (await s.Transactions()).Where(t => t.SettlementDate.Month == 4).ToList();
        Totals(aprilCash).ShouldBe((0, 10000));
    }

    // Toque duplo com a rede lenta, ou duas abas: os dois pedidos leem a fatura ainda em aberto. Só um
    // pagamento pode valer; o outro recebe 409. Várias rodadas, porque a corrida nem sempre acontece.
    [Fact]
    public async Task Paying_the_same_statement_twice_at_once_creates_a_single_payment()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(50000, 5); // faturas de abril a agosto, todas fechadas em 10/08

        foreach (var statement in await s.Statements())
        {
            var responses = await Task.WhenAll(s.Pay(statement.Id, "2026-08-10"), s.Pay(statement.Id, "2026-08-10"));

            responses.Select(r => r.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
            (await s.Transactions()).Count(t => t.StatementId == statement.Id && t.Type == "Transfer").ShouldBe(1);
        }
    }

    [Fact]
    public async Task Statement_payment_rules()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(30000, 3);
        var april = await s.Statement("2026-04");

        await ShouldBeProblem(await s.Pay(april.Id, date: "2026-04-05"), HttpStatusCode.BadRequest,
            "A fatura só pode ser paga depois do fechamento (05/04/2026).");
        await ShouldBeProblem(await s.Pay(april.Id, from: s.Card), HttpStatusCode.BadRequest,
            "A fatura é paga a partir de uma conta, não de um cartão.");
        (await s.Pay(april.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ShouldBeProblem(await s.Pay(april.Id), HttpStatusCode.Conflict, "Esta fatura já está paga.");
    }

    [Fact]
    public async Task A_paid_statement_does_not_change_its_value()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchase = await s.Buy(30000, 3);
        var april = await s.Statement("2026-04");
        await s.Pay(april.Id);

        // Compra nova que cairia em abril.
        var late = await s.Client.PostAsJsonAsync("/transactions", new
        {
            accountId = s.Card, type = "Expense", amountCents = 5000, purchaseDate = "2026-03-20", method = "Credit", installments = 1,
        });
        await ShouldBeProblem(late, HttpStatusCode.BadRequest, "Esta compra cai numa fatura já paga. Desfaça o pagamento para lançá-la.");
        // Excluir a compra com parcela em abril.
        await ShouldBeProblem(await s.Client.DeleteAsync($"/installment-purchases/{purchase[0].InstallmentPurchaseId}"),
            HttpStatusCode.Conflict, "Esta compra está numa fatura paga. Desfaça o pagamento para excluí-la.");
        // Datas de abril.
        await ShouldBeProblem(await s.Client.PatchAsJsonAsync($"/statements/{april.Id}", new { closingDate = "2026-04-03", dueDate = "2026-04-10" }),
            HttpStatusCode.BadRequest, "Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");

        (await s.Statement("2026-04")).TotalCents.ShouldBe(10000);
    }

    [Fact]
    public async Task Undoing_the_payment_unpays_the_statement_and_restoring_pays_it_again()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(30000, 3);
        var april = await s.Statement("2026-04");
        var legs = (await (await s.Pay(april.Id)).Content.ReadFromJsonAsync<List<TransactionDto>>())!;

        (await s.Client.DeleteAsync($"/transactions/{legs[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Statement("2026-04")).IsPaid.ShouldBeFalse();
        (await s.Transactions()).ShouldAllBe(t => t.Type != "Transfer");

        (await s.Client.PostAsync($"/transactions/{legs[1].Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Statement("2026-04")).IsPaid.ShouldBeTrue();
        (await s.Transactions()).Count(t => t.Type == "Transfer").ShouldBe(2);
    }

    [Fact]
    public async Task A_payment_is_not_restored_after_the_statement_changed()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(30000, 3);
        var single = (await s.Buy(5000, 1, "2026-03-20"))[0];
        var april = await s.Statement("2026-04");
        var legs = (await (await s.Pay(april.Id)).Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        await s.Client.DeleteAsync($"/transactions/{legs[0].Id}");
        (await s.Client.DeleteAsync($"/transactions/{single.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeProblem(await s.Client.PostAsync($"/transactions/{legs[0].Id}/restore", null),
            HttpStatusCode.Conflict, "A fatura mudou desde o pagamento. Pague de novo pelo total atual.");
        (await s.Statement("2026-04")).IsPaid.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_user_cannot_pay_or_transfer_with_my_data()
    {
        var (factory, owner) = await Start();
        await using var _ = factory;
        using var __ = owner;
        using var intruder = new Scenario(await factory.CreateAuthenticatedClientAsync());
        intruder.Checking = await intruder.Account(new { name = "Nubank", type = "Checking", initialBalanceCents = 0 });
        await owner.Buy(30000, 3);
        var april = await owner.Statement("2026-04");

        await ShouldBeProblem(await intruder.Pay(april.Id), HttpStatusCode.NotFound, "Fatura não encontrada.");
        await ShouldBeProblem(await intruder.Transfer(owner.Checking, intruder.Checking, 1000), HttpStatusCode.BadRequest, "Conta não encontrada.");
        (await owner.Statement("2026-04")).IsPaid.ShouldBeFalse();
    }
}
