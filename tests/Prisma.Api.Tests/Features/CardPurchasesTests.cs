using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Cartão que fecha dia 5 e vence dia 12 (docs/fase-1.md, 2.1 e 2.2).
[Collection(ApiCollection.Name)]
public sealed class CardPurchasesTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, Guid AccountId, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate,
        Guid? StatementId, string Method, Guid? InstallmentPurchaseId, int? InstallmentNumber);

    private sealed record StatementDto(
        Guid Id, string Reference, DateOnly ClosingDate, DateOnly DueDate, bool IsPaid, long TotalCents);

    private static async Task<Guid> CreateAccount(HttpClient client, object body) =>
        (await (await client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

    private static Task<Guid> CreateCard(HttpClient client) =>
        CreateAccount(client, new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });

    private static Task<HttpResponseMessage> Post(
        HttpClient client, Guid accountId, long amountCents, int installments, string date = "2026-03-10",
        string method = "Credit", string description = "Notebook") =>
        client.PostAsJsonAsync("/transactions", new
        {
            accountId, type = "Expense", amountCents, purchaseDate = date, method, description, installments,
        });

    private static async Task<List<TransactionDto>> Buy(
        HttpClient client, Guid cardId, long amountCents, int installments, string date = "2026-03-10")
    {
        var response = await Post(client, cardId, amountCents, installments, date);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
    }

    private static async Task<List<StatementDto>> Statements(HttpClient client, Guid cardId) =>
        (await client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{cardId}/statements"))!;

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    [Fact]
    public async Task Ten_installments_land_on_the_right_statement_each_month()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);

        var installments = await Buy(client, card, 100005, 10);

        installments.Count.ShouldBe(10);
        installments.Sum(t => t.AmountCents).ShouldBe(100005);
        installments.Select(t => t.AmountCents).ShouldBe([10001, 10001, 10001, 10001, 10001, 10000, 10000, 10000, 10000, 10000]);
        installments.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        installments.ShouldAllBe(t => t.PurchaseDate == new DateOnly(2026, 3, 10) && t.Method == "Credit");
        installments.Select(t => t.InstallmentPurchaseId).Distinct().ShouldHaveSingleItem().ShouldNotBeNull();

        // Compra em 10/03 (após o fechamento de 05/03): de abril/2026 a janeiro/2027, vencendo dia 12.
        installments.Select(t => t.SettlementDate).ShouldBe(
            Enumerable.Range(0, 10).Select(i => new DateOnly(2026, 4, 12).AddMonths(i)));

        var statements = (await Statements(client, card)).OrderBy(s => s.DueDate).ToList();
        statements.Select(s => s.Reference).ShouldBe([
            "2026-04", "2026-05", "2026-06", "2026-07", "2026-08",
            "2026-09", "2026-10", "2026-11", "2026-12", "2027-01",
        ]);
        foreach (var (installment, statement) in installments.Zip(statements))
        {
            installment.StatementId.ShouldBe(statement.Id);
            installment.SettlementDate.ShouldBe(statement.DueDate);
            statement.TotalCents.ShouldBe(installment.AmountCents);
            statement.ClosingDate.ShouldBe(statement.DueDate.AddDays(-7));
        }
    }

    [Fact]
    public async Task Next_purchases_reuse_the_existing_statements()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        await Buy(client, card, 30000, 3);

        var second = await Buy(client, card, 5000, 2, date: "2026-03-20");

        var statements = (await Statements(client, card)).OrderBy(s => s.DueDate).ToList();
        statements.Select(s => s.Reference).ShouldBe(["2026-04", "2026-05", "2026-06"]);
        statements.Select(s => s.TotalCents).ShouldBe([12500, 12500, 10000]);
        second.Select(t => t.StatementId).ShouldBe(statements.Take(2).Select(s => (Guid?)s.Id));
    }

    [Fact]
    public async Task Purchase_on_the_closing_day_enters_the_current_statement()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);

        var single = (await Buy(client, card, 4590, 1, date: "2026-03-05")).ShouldHaveSingleItem();

        single.SettlementDate.ShouldBe(new DateOnly(2026, 3, 12));
        single.InstallmentPurchaseId.ShouldBeNull();
        single.InstallmentNumber.ShouldBeNull();
        (await Statements(client, card)).ShouldHaveSingleItem().Reference.ShouldBe("2026-03");
    }

    [Fact]
    public async Task Invalid_card_purchases_are_rejected_in_portuguese()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        var checking = await CreateAccount(client, new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });

        await ShouldBeProblem(await Post(client, checking, 30000, 3, method: "Pix"),
            HttpStatusCode.BadRequest, "Parcelamento só existe em cartão de crédito.");
        await ShouldBeProblem(await Post(client, card, 30000, 25),
            HttpStatusCode.BadRequest, "O número de parcelas deve estar entre 1 e 24.");
        await ShouldBeProblem(await Post(client, card, 9, 10),
            HttpStatusCode.BadRequest, "O valor total deve ter ao menos 1 centavo por parcela.");

        (await Statements(client, card)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_the_purchase_removes_every_installment()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        var installments = await Buy(client, card, 100000, 10);
        var purchaseId = installments[0].InstallmentPurchaseId!.Value;

        (await client.DeleteAsync($"/installment-purchases/{purchaseId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<List<TransactionDto>>("/transactions"))!.ShouldBeEmpty();
        (await Statements(client, card)).ShouldAllBe(s => s.TotalCents == 0);
        (await client.DeleteAsync($"/installment-purchases/{purchaseId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_single_installment_cannot_be_deleted_alone()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        var installments = await Buy(client, card, 30000, 3);

        await ShouldBeProblem(await client.DeleteAsync($"/transactions/{installments[1].Id}"), HttpStatusCode.Conflict,
            "Esta parcela faz parte de uma compra parcelada. Exclua a compra inteira.");

        (await client.GetFromJsonAsync<List<TransactionDto>>("/transactions"))!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Single_payment_on_the_card_can_be_deleted_and_restored()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        var single = (await Buy(client, card, 4590, 1)).ShouldHaveSingleItem();

        (await client.DeleteAsync($"/transactions/{single.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Statements(client, card)).ShouldHaveSingleItem().TotalCents.ShouldBe(0);

        (await client.PostAsync($"/transactions/{single.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Statements(client, card)).ShouldHaveSingleItem().TotalCents.ShouldBe(4590);
    }

    [Fact]
    public async Task Card_installment_cannot_be_edited_as_a_simple_transaction()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(client);
        var checking = await CreateAccount(client, new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
        var installment = (await Buy(client, card, 30000, 3))[0];

        await ShouldBeProblem(await client.PatchAsJsonAsync($"/transactions/{installment.Id}", new
            {
                accountId = checking, type = "Expense", amountCents = 100, purchaseDate = "2026-03-10", method = "Pix",
            }),
            HttpStatusCode.BadRequest, "Lançamento em cartão de crédito usa a compra no cartão.");
    }

    [Fact]
    public async Task Card_data_of_another_user_is_invisible()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var owner = await factory.CreateAuthenticatedClientAsync();
        using var intruder = await factory.CreateAuthenticatedClientAsync();
        var card = await CreateCard(owner);
        var purchaseId = (await Buy(owner, card, 30000, 3))[0].InstallmentPurchaseId!.Value;

        (await intruder.GetAsync($"/accounts/{card}/statements")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/installment-purchases/{purchaseId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldBeProblem(await Post(intruder, card, 1000, 1), HttpStatusCode.BadRequest, "Conta não encontrada.");

        (await Statements(owner, card)).Sum(s => s.TotalCents).ShouldBe(30000);
    }
}
