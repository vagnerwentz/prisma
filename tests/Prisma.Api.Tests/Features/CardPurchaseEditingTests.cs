using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// Etapa 1.9b. Cartão que fecha dia 5 e vence dia 12; compra em 10/03/2026 (1ª parcela em abril).
[Collection(ApiCollection.Name)]
public sealed class CardPurchaseEditingTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate, Guid? StatementId,
        Guid? CategoryId, string Description, Guid? InstallmentPurchaseId, int? InstallmentNumber);

    private sealed record StatementDto(
        Guid Id, string Reference, DateOnly ClosingDate, DateOnly DueDate, bool DatesEditedManually, long TotalCents);

    private sealed record PurchaseDto(Guid Id, long TotalAmountCents, int InstallmentCount, string Description, List<TransactionDto> Installments);

    private sealed record NodeDto(Guid Id, string Name, string Type, List<IdDto> Subcategories);

    private sealed class Scenario(HttpClient client, Guid cardId) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid CardId { get; } = cardId;

        public async Task<List<TransactionDto>> Buy(long amountCents, int installments, string date = "2026-03-10", string description = "Notebook")
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = CardId, type = "Expense", amountCents, purchaseDate = date, method = "Credit", description, installments,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        }

        public async Task<List<TransactionDto>> Transactions() =>
            (await Client.GetFromJsonAsync<List<TransactionDto>>("/transactions"))!.OrderBy(t => t.SettlementDate).ToList();

        public async Task<List<StatementDto>> Statements() =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{CardId}/statements"))!.OrderBy(s => s.DueDate).ToList();

        public Task<HttpResponseMessage> PatchTransaction(TransactionDto t, long? amount = null, Guid? categoryId = null, string? description = null) =>
            Client.PatchAsJsonAsync($"/transactions/{t.Id}", new
            {
                accountId = CardId, type = "Expense", amountCents = amount ?? t.AmountCents, purchaseDate = t.PurchaseDate,
                categoryId = categoryId ?? t.CategoryId, method = "Credit", description = description ?? t.Description,
            });

        public Task<HttpResponseMessage> PatchPurchase(
            Guid purchaseId, long total, int count, Guid? categoryId = null, string description = "Notebook", string date = "2026-03-10") =>
            Client.PatchAsJsonAsync($"/installment-purchases/{purchaseId}",
                new { totalAmountCents = total, installmentCount = count, categoryId, description, purchaseDate = date });

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        var client = await factory.CreateAuthenticatedClientAsync();
        var card = await (await client.PostAsJsonAsync("/accounts", new
        {
            name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12,
        })).Content.ReadFromJsonAsync<IdDto>();
        return (factory, new Scenario(client, card!.Id));
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    [Fact]
    public async Task Editing_installment_3_does_not_change_the_others()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var installments = await s.Buy(100000, 10);
        var tree = (await s.Client.GetFromJsonAsync<List<NodeDto>>("/categories"))!;
        var home = tree.Single(c => c.Type == "Expense" && c.Name == "Compras").Id;

        var response = await s.PatchTransaction(installments[2], categoryId: home, description: "Notebook — parcela 3");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await s.Transactions();
        after.Single(t => t.Id == installments[2].Id).ShouldSatisfyAllConditions(
            t => t.CategoryId.ShouldBe(home),
            t => t.Description.ShouldBe("Notebook — parcela 3"),
            t => t.AmountCents.ShouldBe(10000));
        after.Where(t => t.Id != installments[2].Id).ShouldAllBe(t => t.Description == "Notebook" && t.CategoryId == null);
        after.Sum(t => t.AmountCents).ShouldBe(100000);
    }

    [Fact]
    public async Task Installment_amount_is_refused_but_a_single_payment_amount_is_accepted()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var installment = (await s.Buy(100000, 10))[2];
        var single = (await s.Buy(4590, 1, date: "2026-03-20", description: "Padaria"))[0];

        await ShouldBeProblem(await s.PatchTransaction(installment, amount: 12000), HttpStatusCode.BadRequest,
            "O valor de uma parcela muda pela compra inteira, para a soma continuar igual ao total.");

        (await s.PatchTransaction(single, amount: 4990)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Statements())[0].TotalCents.ShouldBe(10000 + 4990);
    }

    [Fact]
    public async Task Changing_the_total_redistributes_without_losing_a_cent()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(100000, 10))[0].InstallmentPurchaseId!.Value;

        var response = await s.PatchPurchase(purchaseId, 120003, 10);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var purchase = (await response.Content.ReadFromJsonAsync<PurchaseDto>())!;
        purchase.TotalAmountCents.ShouldBe(120003);
        purchase.Installments.Select(t => t.AmountCents).ShouldBe([12001, 12001, 12001, 12000, 12000, 12000, 12000, 12000, 12000, 12000]);
        (await s.Transactions()).Sum(t => t.AmountCents).ShouldBe(120003);
        (await s.Statements()).Select(st => st.TotalCents).ShouldBe([12001, 12001, 12001, 12000, 12000, 12000, 12000, 12000, 12000, 12000]);
    }

    // --- Duas operações ao mesmo tempo abrindo a mesma fatura (toque duplo, dois aparelhos) ---
    // As duas leem que a fatura do mês ainda não existe e tentam criá-la; o índice único deixa
    // passar só uma. A outra precisa usar a fatura recém-criada, não falhar. Várias rodadas,
    // porque a corrida nem sempre acontece.

    private static async Task<List<TransactionDto>> Created(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
    }

    [Fact]
    public async Task Two_purchases_at_once_share_the_statement_they_both_open()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var month = 1; month <= 6; month++)
        {
            var date = $"2027-{month:00}-10";
            Task<HttpResponseMessage> Buy() => s.Client.PostAsJsonAsync("/transactions", new
            {
                accountId = s.CardId, type = "Expense", amountCents = 1000, purchaseDate = date, method = "Credit", installments = 1,
            });

            var responses = await Task.WhenAll(Buy(), Buy());

            var first = (await Created(responses[0])).Single();
            var second = (await Created(responses[1])).Single();
            second.StatementId.ShouldBe(first.StatementId);
        }
        (await s.Statements()).Select(st => st.Reference).ShouldBe(Enumerable.Range(2, 6).Select(m => $"2027-{m:00}"));
    }

    [Fact]
    public async Task Two_purchases_moved_at_once_to_a_new_month_share_its_statement()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var a = (await s.Buy(1000, 1)).Single();
        var b = (await s.Buy(2000, 1)).Single();

        for (var month = 1; month <= 6; month++)
        {
            var date = $"2028-{month:00}-10";
            Task<HttpResponseMessage> Move(TransactionDto t) => s.Client.PatchAsJsonAsync($"/transactions/{t.Id}", new
            {
                accountId = s.CardId, type = "Expense", amountCents = t.AmountCents, purchaseDate = date, method = "Credit",
                description = t.Description,
            });

            var responses = await Task.WhenAll(Move(a), Move(b));

            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
            var moved = (await s.Transactions()).Where(t => t.PurchaseDate == DateOnly.Parse(date)).ToList();
            moved.Count.ShouldBe(2);
            moved.Select(t => t.StatementId).Distinct().ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task Two_purchases_extended_at_once_share_the_statements_they_open()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var a = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;
        var b = (await s.Buy(60000, 3))[0].InstallmentPurchaseId!.Value;

        foreach (var count in new[] { 6, 9, 12, 15 })
        {
            var responses = await Task.WhenAll(s.PatchPurchase(a, 30000, count), s.PatchPurchase(b, 60000, count));

            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        }
        var statements = await s.Statements();
        statements.Count.ShouldBe(15);
        statements.Select(st => st.Reference).Distinct().Count().ShouldBe(15);
        statements.Sum(st => st.TotalCents).ShouldBe(90000);
    }

    [Fact]
    public async Task Fewer_installments_remove_the_last_ones()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(100005, 10))[0].InstallmentPurchaseId!.Value;

        (await s.PatchPurchase(purchaseId, 100005, 3)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await s.Transactions();
        after.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3]);
        after.Select(t => t.AmountCents).ShouldBe([33335, 33335, 33335]);
        (await s.Statements()).Select(st => st.TotalCents).ShouldBe([33335, 33335, 33335, 0, 0, 0, 0, 0, 0, 0]);
    }

    [Fact]
    public async Task More_installments_open_the_following_statements()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;

        (await s.PatchPurchase(purchaseId, 30000, 5, description: "Notebook em 5x")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await s.Transactions();
        after.Select(t => t.SettlementDate).ShouldBe(Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 4, 12).AddMonths(i)));
        after.ShouldAllBe(t => t.AmountCents == 6000 && t.Description == "Notebook em 5x");
        (await s.Statements()).Select(st => st.Reference).ShouldBe(["2026-04", "2026-05", "2026-06", "2026-07", "2026-08"]);
    }

    [Fact]
    public async Task Invalid_purchase_edit_is_rejected_and_changes_nothing()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;

        await ShouldBeProblem(await s.PatchPurchase(purchaseId, 30000, 25), HttpStatusCode.BadRequest,
            "O número de parcelas deve estar entre 1 e 24.");
        await ShouldBeProblem(await s.PatchPurchase(purchaseId, 2, 3), HttpStatusCode.BadRequest,
            "O valor total deve ter ao menos 1 centavo por parcela não paga.");

        (await s.Transactions()).Select(t => t.AmountCents).ShouldBe([10000, 10000, 10000]);
    }

    [Fact]
    public async Task Editing_statement_dates_recalculates_the_settlement_of_its_transactions()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(20000, 2);
        await s.Buy(4590, 1, date: "2026-03-20", description: "Padaria");
        var april = (await s.Statements())[0];

        var response = await s.Client.PatchAsJsonAsync($"/statements/{april.Id}",
            new { closingDate = "2026-04-06", dueDate = "2026-04-13" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = (await response.Content.ReadFromJsonAsync<StatementDto>())!;
        edited.DatesEditedManually.ShouldBeTrue();
        edited.TotalCents.ShouldBe(10000 + 4590);

        var after = await s.Transactions();
        after.Where(t => t.StatementId == april.Id).ShouldAllBe(t => t.SettlementDate == new DateOnly(2026, 4, 13));
        after.Where(t => t.StatementId != april.Id).ShouldAllBe(t => t.SettlementDate == new DateOnly(2026, 5, 12));
    }

    // Regra 5 (docs/fase-1.md, 2.1) de ponta a ponta: com o fechamento de abril adiado para 06/04,
    // uma compra em 06/04 ainda entra em abril, com o vencimento editado.
    [Fact]
    public async Task New_purchases_follow_the_edited_statement_dates()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(4590, 1);
        var april = (await s.Statements())[0];
        await s.Client.PatchAsJsonAsync($"/statements/{april.Id}", new { closingDate = "2026-04-06", dueDate = "2026-04-13" });

        var purchase = (await s.Buy(1000, 1, date: "2026-04-06", description: "Farmácia"))[0];

        purchase.StatementId.ShouldBe(april.Id);
        purchase.SettlementDate.ShouldBe(new DateOnly(2026, 4, 13));
    }

    [Fact]
    public async Task Due_date_before_closing_date_is_rejected()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(4590, 1);
        var april = (await s.Statements())[0];

        await ShouldBeProblem(
            await s.Client.PatchAsJsonAsync($"/statements/{april.Id}", new { closingDate = "2026-04-13", dueDate = "2026-04-12" }),
            HttpStatusCode.BadRequest, "O vencimento não pode ser antes do fechamento.");
        (await s.Transactions()).Single().SettlementDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public async Task Purchases_and_statements_of_another_user_are_invisible()
    {
        var (factory, owner) = await Start();
        await using var _ = factory;
        using var __ = owner;
        using var intruder = await factory.CreateAuthenticatedClientAsync();
        var purchaseId = (await owner.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;
        var april = (await owner.Statements())[0];

        (await intruder.PatchAsJsonAsync($"/installment-purchases/{purchaseId}",
            new { totalAmountCents = 1, installmentCount = 1, purchaseDate = "2026-03-10" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.PatchAsJsonAsync($"/statements/{april.Id}",
            new { closingDate = "2026-04-01", dueDate = "2026-04-02" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Transactions()).Sum(t => t.AmountCents).ShouldBe(30000);
        (await owner.Statements())[0].DueDate.ShouldBe(new DateOnly(2026, 4, 12));
    }

    // Etapa 1.14: "Desfazer" depois de excluir a compra inteira.
    [Fact]
    public async Task Restoring_a_deleted_purchase_brings_back_every_installment()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(100005, 10))[0].InstallmentPurchaseId!.Value;
        (await s.Client.DeleteAsync($"/installment-purchases/{purchaseId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Transactions()).ShouldBeEmpty();

        var response = await s.Client.PostAsync($"/installment-purchases/{purchaseId}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = (await response.Content.ReadFromJsonAsync<PurchaseDto>())!;
        restored.Installments.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var after = await s.Transactions();
        after.Count.ShouldBe(10);
        after.Sum(t => t.AmountCents).ShouldBe(100005);
        (await s.Statements()).Select(st => st.TotalCents).ShouldBe([10001, 10001, 10001, 10001, 10001, 10000, 10000, 10000, 10000, 10000]);
    }

    [Fact]
    public async Task Restoring_does_not_bring_back_installments_removed_by_an_earlier_edit()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(100005, 10))[0].InstallmentPurchaseId!.Value;
        (await s.PatchPurchase(purchaseId, 100005, 3)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await s.Client.DeleteAsync($"/installment-purchases/{purchaseId}");

        (await s.Client.PostAsync($"/installment-purchases/{purchaseId}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await s.Transactions();
        after.Select(t => t.InstallmentNumber).ShouldBe([1, 2, 3]);
        after.Sum(t => t.AmountCents).ShouldBe(100005);
    }

    [Fact]
    public async Task Restoring_a_purchase_that_is_not_deleted_returns_404()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;

        await ShouldBeProblem(await s.Client.PostAsync($"/installment-purchases/{purchaseId}/restore", null),
            HttpStatusCode.NotFound, "Compra parcelada excluída não encontrada.");
    }

    [Fact]
    public async Task Restoring_a_purchase_whose_card_was_deleted_returns_409()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;
        await s.Client.DeleteAsync($"/installment-purchases/{purchaseId}");
        (await s.Client.DeleteAsync($"/accounts/{s.CardId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeProblem(await s.Client.PostAsync($"/installment-purchases/{purchaseId}/restore", null),
            HttpStatusCode.Conflict, "A conta desta compra foi excluída; não é possível restaurá-la.");
    }

    [Fact]
    public async Task Another_user_cannot_restore_a_purchase()
    {
        var (factory, owner) = await Start();
        await using var _ = factory;
        using var __ = owner;
        using var intruder = await factory.CreateAuthenticatedClientAsync();
        var purchaseId = (await owner.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;
        await owner.Client.DeleteAsync($"/installment-purchases/{purchaseId}");

        (await intruder.PostAsync($"/installment-purchases/{purchaseId}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Transactions()).ShouldBeEmpty();
    }

    // Etapa 1.14b: mudar a data da compra no cartão.
    [Fact]
    public async Task Moving_a_single_card_purchase_changes_its_statement()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var single = (await s.Buy(5590, 1, description: "Netflix"))[0];

        var response = await s.Client.PatchAsJsonAsync($"/transactions/{single.Id}", new
        {
            accountId = s.CardId, type = "Expense", amountCents = 5590, purchaseDate = "2026-03-04",
            method = "Credit", description = "Netflix",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = (await s.Transactions()).Single();
        moved.PurchaseDate.ShouldBe(new DateOnly(2026, 3, 4));
        moved.SettlementDate.ShouldBe(new DateOnly(2026, 3, 12));
        var statements = await s.Statements();
        statements.Select(st => (st.Reference, st.TotalCents)).ShouldBe([("2026-03", 5590L), ("2026-04", 0L)]);
    }

    [Fact]
    public async Task Moving_an_installment_purchase_moves_every_installment()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(100000, 3))[0].InstallmentPurchaseId!.Value;

        var response = await s.PatchPurchase(purchaseId, 100000, 3, date: "2026-05-10");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await s.Transactions();
        after.Select(t => t.SettlementDate).ShouldBe([new DateOnly(2026, 6, 12), new DateOnly(2026, 7, 12), new DateOnly(2026, 8, 12)]);
        after.ShouldAllBe(t => t.PurchaseDate == new DateOnly(2026, 5, 10));
        after.Sum(t => t.AmountCents).ShouldBe(100000);
        (await s.Statements()).Where(st => st.TotalCents > 0).Select(st => st.Reference).ShouldBe(["2026-06", "2026-07", "2026-08"]);
    }

    [Fact]
    public async Task Installment_date_changes_only_through_the_whole_purchase()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var second = (await s.Buy(30000, 3)).Single(t => t.InstallmentNumber == 2);

        var response = await s.Client.PatchAsJsonAsync($"/transactions/{second.Id}", new
        {
            accountId = s.CardId, type = "Expense", amountCents = second.AmountCents, purchaseDate = "2026-05-10",
            method = "Credit", description = "Notebook",
        });

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "A data de uma parcela muda pela compra inteira.");
    }

    [Fact]
    public async Task Purchase_edit_without_a_date_is_rejected()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(30000, 3))[0].InstallmentPurchaseId!.Value;

        var response = await s.Client.PatchAsJsonAsync($"/installment-purchases/{purchaseId}",
            new { totalAmountCents = 30000, installmentCount = 3 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem!.Errors["PurchaseDate"].ShouldContain("Informe a data da compra.");
        (await s.Transactions()).ShouldAllBe(t => t.PurchaseDate == new DateOnly(2026, 3, 10));
    }
}
