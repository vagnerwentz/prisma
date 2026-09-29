using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.13 (etapa 2.24): trocar o cartão de uma compra pelo PATCH da compra à vista
// (/transactions/{id}) e da parcelada (/installment-purchases/{id}); e o StatementTouch na troca de
// data (regra 7). Visa "fecha 26, vence 5"; Master "fecha 5, vence 12". Compra em 28/10/2026.
[Collection(ApiCollection.Name)]
public sealed class CardChangeTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate,
        Guid? StatementId, Guid? InstallmentPurchaseId, int? InstallmentNumber);

    private sealed record StatementDto(Guid Id, string Reference, bool IsPaid, long TotalCents);

    private sealed record PurchaseDto(Guid Id, Guid AccountId, List<TransactionDto> Installments);

    private sealed record SummaryDto(string Month, long ExpenseCents);

    private sealed class Scenario(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid Visa { get; private set; }
        public Guid Master { get; private set; }

        public async Task Setup()
        {
            Checking = await Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            await NewCards();
        }

        public async Task NewCards()
        {
            Visa = await Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });
            Master = await Account(new { name = "Master", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
        }

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public async Task<List<TransactionDto>> Buy(Guid card, long amount, string date, int installments = 1)
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = card, type = "Expense", amountCents = amount, purchaseDate = date, method = "Credit",
                description = "Fone", installments,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        }

        public Task<HttpResponseMessage> ChangeSingle(TransactionDto t, Guid account, string? date = null) =>
            Client.PatchAsJsonAsync($"/transactions/{t.Id}", new
            {
                accountId = account, type = "Expense", amountCents = t.AmountCents,
                purchaseDate = date ?? t.PurchaseDate.ToString("yyyy-MM-dd"), method = "Credit", description = "Fone",
            });

        public Task<HttpResponseMessage> ChangePurchase(Guid purchaseId, Guid? account, long total = 10000, int count = 3) =>
            Client.PatchAsJsonAsync($"/installment-purchases/{purchaseId}", new
            {
                totalAmountCents = total, installmentCount = count, description = "Fone", purchaseDate = "2026-10-28",
                accountId = account,
            });

        public Task<HttpResponseMessage> Refund(Guid card, Guid purchase) =>
            Client.PostAsJsonAsync("/transactions", new
            {
                accountId = card, type = "Refund", amountCents = 2000, purchaseDate = "2026-10-30", method = "Credit",
                description = "Estorno", refundedTransactionId = purchase,
            });

        public Task<HttpResponseMessage> Pay(Guid statementId, string date) =>
            Client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = Checking, date, method = "Boleto" });

        public async Task<StatementDto?> Statement(Guid card, string reference) =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{card}/statements"))!.SingleOrDefault(s => s.Reference == reference);

        public async Task<List<TransactionDto>> In(Guid statementId) =>
            (await Client.GetFromJsonAsync<List<TransactionDto>>($"/transactions?statementId={statementId}"))!;

        public async Task<TransactionDto> Get(Guid id) => (await Client.GetFromJsonAsync<TransactionDto>($"/transactions/{id}"))!;

        public async Task<SummaryDto> Summary(string month) =>
            (await Client.GetFromJsonAsync<SummaryDto>($"/dashboard/summary?month={month}"))!;

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString);
        var s = new Scenario(await factory.CreateAuthenticatedClientAsync());
        await s.Setup();
        return (factory, s);
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.ShouldBe(detail);
    }

    // O caso do usuário: lançou no Visa, era no Master. A despesa sai de dezembro e entra em novembro,
    // na fatura do Master que já existe (nunca uma segunda fatura 2026-11).
    [Fact]
    public async Task A_single_purchase_changes_card_end_to_end()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchase = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
        await s.Buy(s.Master, 4000, "2026-10-20");
        (await s.Summary("2026-12")).ExpenseCents.ShouldBe(8000);

        var response = await s.ChangeSingle(purchase, s.Master);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changed = await s.Get(purchase.Id);
        changed.AccountId.ShouldBe(s.Master);
        changed.PurchaseDate.ShouldBe(new DateOnly(2026, 10, 28));
        changed.SettlementDate.ShouldBe(new DateOnly(2026, 11, 12));
        var masterNovember = (await s.Statement(s.Master, "2026-11"))!;
        changed.StatementId.ShouldBe(masterNovember.Id);
        masterNovember.TotalCents.ShouldBe(12000);
        (await s.Statement(s.Visa, "2026-12"))!.TotalCents.ShouldBe(0);
        (await s.Summary("2026-12")).ExpenseCents.ShouldBe(0);
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(12000);
    }

    [Fact]
    public async Task An_installment_purchase_changes_card_whole()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(s.Visa, 10000, "2026-10-28", 3))[0].InstallmentPurchaseId!.Value;
        await s.Buy(s.Master, 4000, "2026-10-20");

        var response = await s.ChangePurchase(purchaseId, s.Master);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changed = (await response.Content.ReadFromJsonAsync<PurchaseDto>())!;
        changed.AccountId.ShouldBe(s.Master);
        changed.Installments.ShouldAllBe(t => t.AccountId == s.Master);
        changed.Installments.Select(t => t.SettlementDate)
            .ShouldBe([new DateOnly(2026, 11, 12), new DateOnly(2026, 12, 12), new DateOnly(2027, 1, 12)]);
        changed.Installments.Select(t => t.AmountCents).ShouldBe([3334L, 3333L, 3333L]);
        foreach (var (reference, cents) in new[] { ("2026-11", 7334L), ("2026-12", 3333L), ("2027-01", 3333L) })
            (await s.Statement(s.Master, reference))!.TotalCents.ShouldBe(cents);
        foreach (var reference in new[] { "2026-12", "2027-01", "2027-02" })
            (await s.Statement(s.Visa, reference))!.TotalCents.ShouldBe(0);
    }

    // A tela aberta antes do deploy da 2.24 não manda o cartão: a compra fica onde está.
    [Fact]
    public async Task An_installment_purchase_edited_without_a_card_keeps_its_card()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchaseId = (await s.Buy(s.Visa, 10000, "2026-10-28", 3))[0].InstallmentPurchaseId!.Value;

        var response = await s.ChangePurchase(purchaseId, account: null, total: 12000);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changed = (await response.Content.ReadFromJsonAsync<PurchaseDto>())!;
        changed.AccountId.ShouldBe(s.Visa);
        changed.Installments.ShouldAllBe(t => t.AccountId == s.Visa);
        (await s.Statement(s.Visa, "2026-12"))!.TotalCents.ShouldBe(4000);
    }

    // Regra 4: o estorno ficaria no Visa, abatendo uma compra que não está mais lá.
    [Fact]
    public async Task A_purchase_with_a_refund_keeps_its_card()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var single = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
        var installments = await s.Buy(s.Visa, 10000, "2026-10-28", 3);
        (await s.Refund(s.Visa, single.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await s.Refund(s.Visa, installments[1].Id)).StatusCode.ShouldBe(HttpStatusCode.Created);

        const string message = "Esta despesa tem estornos; o tipo e a conta não mudam.";
        await ShouldBeProblem(await s.ChangeSingle(single, s.Master), HttpStatusCode.BadRequest, message);
        await ShouldBeProblem(await s.ChangePurchase(installments[0].InstallmentPurchaseId!.Value, s.Master),
            HttpStatusCode.BadRequest, message);

        (await s.Get(single.Id)).AccountId.ShouldBe(s.Visa);
        (await s.Get(installments[0].Id)).AccountId.ShouldBe(s.Visa);
    }

    [Fact]
    public async Task A_purchase_only_changes_to_another_card()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var single = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
        var purchaseId = (await s.Buy(s.Visa, 10000, "2026-10-28", 3))[0].InstallmentPurchaseId!.Value;

        const string message = "Compra no cartão só troca para outro cartão. Para usar outra conta, exclua e lance de novo.";
        await ShouldBeProblem(await s.ChangeSingle(single, s.Checking), HttpStatusCode.BadRequest, message);
        await ShouldBeProblem(await s.ChangePurchase(purchaseId, s.Checking), HttpStatusCode.BadRequest, message);
    }

    [Fact]
    public async Task Another_users_card_is_not_found()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var s = new Scenario(await factory.CreateAuthenticatedClientAsync());
        using var other = new Scenario(await factory.CreateAuthenticatedClientAsync());
        await s.Setup();
        await other.Setup();
        var single = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
        var purchaseId = (await s.Buy(s.Visa, 10000, "2026-10-28", 3))[0].InstallmentPurchaseId!.Value;

        await ShouldBeProblem(await s.ChangeSingle(single, other.Master), HttpStatusCode.BadRequest, "Conta não encontrada.");
        await ShouldBeProblem(await s.ChangePurchase(purchaseId, other.Master), HttpStatusCode.BadRequest, "Conta não encontrada.");

        (await other.Statement(other.Master, "2026-11")).ShouldBeNull();
    }

    // Regra 7: trocar o cartão enquanto a fatura de origem é paga. A fatura paga nunca fica com valor
    // diferente do pagamento. Várias rodadas, cada uma em cartões novos, porque a corrida nem sempre acontece.
    [Fact]
    public async Task Changing_card_while_the_source_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCards();
            var single = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
            await s.Buy(s.Visa, 4000, "2026-11-10");
            var december = (await s.Statement(s.Visa, "2026-12"))!;

            var responses = await Task.WhenAll(s.ChangeSingle(single, s.Master), s.Pay(december.Id, "2026-11-27"));

            responses[0].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            responses[1].StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.IsSuccessStatusCode);

            december = (await s.Statement(s.Visa, "2026-12"))!;
            if (december.IsPaid)
                (await s.In(december.Id)).Single(t => t.Type == "Transfer").AmountCents.ShouldBe(december.TotalCents);
            else
                december.TotalCents.ShouldBe(4000);
        }
    }

    // A parcelada que chega a uma fatura do cartão novo enquanto ela é paga.
    [Fact]
    public async Task Changing_card_while_the_destination_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCards();
            var purchaseId = (await s.Buy(s.Visa, 10000, "2026-10-28", 3))[0].InstallmentPurchaseId!.Value;
            await s.Buy(s.Master, 4000, "2026-10-20");
            var november = (await s.Statement(s.Master, "2026-11"))!;

            var responses = await Task.WhenAll(s.ChangePurchase(purchaseId, s.Master), s.Pay(november.Id, "2026-11-06"));

            responses[0].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            responses[1].StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.IsSuccessStatusCode);

            november = (await s.Statement(s.Master, "2026-11"))!;
            if (november.IsPaid)
                (await s.In(november.Id)).Single(t => t.Type == "Transfer").AmountCents.ShouldBe(november.TotalCents);
            else
                november.TotalCents.ShouldBe(7334);
        }
    }

    // A falha que a 2.24 corrige junto: trocar a data leva a compra para uma fatura que está sendo paga.
    [Fact]
    public async Task Changing_the_date_while_the_destination_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCards();
            var single = (await s.Buy(s.Visa, 8000, "2026-10-28")).Single();
            await s.Buy(s.Visa, 4000, "2026-10-10");
            var november = (await s.Statement(s.Visa, "2026-11"))!;

            var responses = await Task.WhenAll(s.ChangeSingle(single, s.Visa, "2026-10-20"), s.Pay(november.Id, "2026-10-27"));

            responses[0].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            responses[1].StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.IsSuccessStatusCode);

            november = (await s.Statement(s.Visa, "2026-11"))!;
            if (november.IsPaid)
                (await s.In(november.Id)).Single(t => t.Type == "Transfer").AmountCents.ShouldBe(november.TotalCents);
            else
                november.TotalCents.ShouldBe(12000);
        }
    }
}
