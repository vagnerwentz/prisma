using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.9 (etapa 2.20): POST /transactions/{id}/move-statement (regras 2, 3 e 6, tarefa 5)
// e PATCH /statements/{id} movendo compras (regras 4, 5 e 6, tarefa 6).
// Cartão "fecha 26, vence 5", como o do dono: a fatura 2026-10 fecha em 26/09 e vence em 05/10; a
// 2026-11 fecha em 26/10 e vence em 05/11.
[Collection(ApiCollection.Name)]
public sealed class StatementAlignmentTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, string Type, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate, Guid? StatementId,
        Guid? InstallmentPurchaseId, int? InstallmentNumber, bool StatementPinned);

    private sealed record StatementDto(Guid Id, string Reference, DateOnly ClosingDate, DateOnly DueDate, bool IsPaid, long TotalCents);

    private sealed record EditedDto(Guid Id, string Reference, DateOnly ClosingDate, DateOnly DueDate, long TotalCents, int MovedPurchases);

    private sealed record PreviewItemDto(
        Guid TransactionId, string Type, string Description, DateOnly PurchaseDate, long AmountCents,
        int InstallmentCount, string FromReference, string ToReference);

    private sealed record PreviewDto(List<PreviewItemDto> MovedPurchases);

    private sealed record SummaryDto(string Month, long ExpenseCents, long CardExpenseCents);

    private sealed class Scenario(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid Card { get; private set; }

        public async Task Setup()
        {
            Checking = await Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            await NewCard();
        }

        public async Task NewCard() =>
            Card = await Account(new { name = "Itaú Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public async Task<List<TransactionDto>> Buy(long amount, string date, int installments = 1)
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = Card, type = "Expense", amountCents = amount, purchaseDate = date, method = "Credit",
                description = "Lanche", installments,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        }

        public Task<HttpResponseMessage> Move(Guid transactionId, string direction = "Next") =>
            Client.PostAsJsonAsync($"/transactions/{transactionId}/move-statement", new { direction });

        public async Task<List<TransactionDto>> Moved(Guid transactionId, string direction = "Next")
        {
            var response = await Move(transactionId, direction);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!;
        }

        public async Task<HttpResponseMessage> EditDates(string reference, string closingDate, string dueDate) =>
            await Client.PatchAsJsonAsync($"/statements/{(await Statement(reference)).Id}", new { closingDate, dueDate });

        public async Task<EditedDto> Edited(string reference, string closingDate, string dueDate)
        {
            var response = await EditDates(reference, closingDate, dueDate);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<EditedDto>())!;
        }

        public async Task<HttpResponseMessage> Preview(string reference, string closingDate, string dueDate) =>
            await Client.PostAsJsonAsync($"/statements/{(await Statement(reference)).Id}/date-preview", new { closingDate, dueDate });

        public async Task<PreviewDto> Previewed(string reference, string closingDate, string dueDate)
        {
            var response = await Preview(reference, closingDate, dueDate);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<PreviewDto>())!;
        }

        public async Task<TransactionDto> Get(Guid id) => (await Client.GetFromJsonAsync<TransactionDto>($"/transactions/{id}"))!;

        public Task<HttpResponseMessage> Pay(Guid statementId, string date) =>
            Client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = Checking, date, method = "Boleto" });

        public async Task<StatementDto> Statement(string reference) =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{Card}/statements"))!.Single(s => s.Reference == reference);

        public async Task<List<TransactionDto>> In(Guid statementId) =>
            (await Client.GetFromJsonAsync<List<TransactionDto>>($"/transactions?statementId={statementId}"))!;

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

    // Caso 1 da tabela, de ponta a ponta: os lanches de 25/09/2026 foram para a fatura de novembro no Itaú.
    [Fact]
    public async Task The_snacks_move_to_the_november_statement_and_the_cash_view_follows()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var first = (await s.Buy(3000, "2026-09-25")).Single();
        var second = (await s.Buy(2550, "2026-09-25")).Single();
        await s.Buy(10000, "2026-09-10");
        var october10 = (await s.Buy(4000, "2026-10-10")).Single();
        (await s.Statement("2026-10")).TotalCents.ShouldBe(15550);
        (await s.Summary("2026-10")).ExpenseCents.ShouldBe(15550);
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(4000);

        var moved = (await s.Moved(first.Id)).Single();
        await s.Moved(second.Id);

        var november = await s.Statement("2026-11");
        moved.StatementId.ShouldBe(november.Id);
        moved.SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
        moved.PurchaseDate.ShouldBe(new DateOnly(2026, 9, 25));
        moved.StatementPinned.ShouldBeTrue();
        (await s.Statement("2026-10")).TotalCents.ShouldBe(10000);
        november.TotalCents.ShouldBe(9550);
        (await s.In(november.Id)).Select(t => t.Id).ShouldBe([first.Id, second.Id, october10.Id], ignoreOrder: true);
        (await s.Summary("2026-10")).ExpenseCents.ShouldBe(10000);
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(9550);
    }

    // Caso 2 da tabela: o "Desfazer" devolve a compra e a solta (regra 3).
    [Fact]
    public async Task Moving_back_releases_the_purchase()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var snack = (await s.Buy(3000, "2026-09-25")).Single();
        await s.Moved(snack.Id);

        var back = (await s.Moved(snack.Id, "Previous")).Single();

        back.StatementId.ShouldBe((await s.Statement("2026-10")).Id);
        back.SettlementDate.ShouldBe(new DateOnly(2026, 10, 5));
        back.StatementPinned.ShouldBeFalse();
        (await s.In(back.StatementId!.Value)).Single().StatementPinned.ShouldBeFalse();
    }

    // Caso 3 da tabela, a partir da parcela 2: a compra anda inteira.
    [Fact]
    public async Task An_installment_purchase_moves_whole_from_any_installment()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var installments = await s.Buy(10000, "2026-09-20", installments: 3);

        var moved = await s.Moved(installments[1].Id);

        moved.OrderBy(t => t.InstallmentNumber).Select(t => t.SettlementDate).ShouldBe([
            new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 5), new DateOnly(2027, 1, 5),
        ]);
        moved.OrderBy(t => t.InstallmentNumber).Select(t => t.AmountCents).ShouldBe([3334, 3333, 3333]);
        moved.ShouldAllBe(t => t.StatementPinned && t.PurchaseDate == new DateOnly(2026, 9, 20));
        (await s.Statement("2026-10")).TotalCents.ShouldBe(0);
        (await s.Statement("2027-01")).TotalCents.ShouldBe(3333);
    }

    [Fact]
    public async Task Refusals_come_in_portuguese()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var snack = (await s.Buy(3000, "2026-09-25")).Single();
        await s.Buy(4000, "2026-10-10");
        var paid = (await s.Pay((await s.Statement("2026-11")).Id, "2026-10-27")).StatusCode;
        paid.ShouldBe(HttpStatusCode.Created);

        await ShouldBeProblem(await s.Move(snack.Id), HttpStatusCode.BadRequest, "A fatura seguinte já está paga.");
        await ShouldBeProblem(await s.Move(Guid.NewGuid()), HttpStatusCode.NotFound, "Transação não encontrada.");
        (await s.Move(snack.Id, "Sideways")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Statement("2026-11")).TotalCents.ShouldBe(4000);
    }

    // Regra 6: mover para a fatura que está sendo paga. A fatura paga nunca fica com valor diferente do
    // pagamento. Várias rodadas, cada uma num cartão novo, porque a corrida nem sempre acontece.
    [Fact]
    public async Task Moving_into_a_statement_being_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCard();
            var snack = (await s.Buy(3000, "2026-09-25")).Single();
            await s.Buy(4000, "2026-10-10");
            var november = await s.Statement("2026-11");

            var responses = await Task.WhenAll(s.Move(snack.Id), s.Pay(november.Id, "2026-10-27"));

            responses[1].StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            responses[0].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.IsSuccessStatusCode);

            // Em sequência, os dois valem (o pagamento já vê o lanche); na corrida, um recebe 409.
            november = await s.Statement("2026-11");
            if (november.IsPaid)
                (await s.In(november.Id)).Single(t => t.Type == "Transfer").AmountCents.ShouldBe(november.TotalCents);
            else
                november.TotalCents.ShouldBe(7000);
        }
    }

    [Fact]
    public async Task Another_user_cannot_move_the_purchase()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var snack = (await s.Buy(3000, "2026-09-25")).Single();
        using var stranger = await factory.CreateAuthenticatedClientAsync();

        var response = await stranger.PostAsJsonAsync($"/transactions/{snack.Id}/move-statement", new { direction = "Next" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Statement("2026-10")).TotalCents.ShouldBe(3000);
    }

    // ---------- Editar as datas da fatura (tarefa 6) ----------

    // Caso 5 da tabela.
    [Fact]
    public async Task A_later_closing_brings_the_purchase_back_from_the_next_statement()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var late = (await s.Buy(4590, "2026-10-27")).Single();
        await s.Buy(1000, "2026-10-20");
        (await s.Statement("2026-12")).TotalCents.ShouldBe(4590);

        var edited = await s.Edited("2026-11", "2026-10-27", "2026-11-05");

        edited.MovedPurchases.ShouldBe(1);
        edited.TotalCents.ShouldBe(5590);
        (await s.Statement("2026-12")).TotalCents.ShouldBe(0);
        var moved = await s.Get(late.Id);
        moved.StatementId.ShouldBe(edited.Id);
        moved.SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
        moved.PurchaseDate.ShouldBe(new DateOnly(2026, 10, 27));
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(5590);
        (await s.Summary("2026-12")).ExpenseCents.ShouldBe(0);
    }

    // Caso 6 da tabela: a fatura seguinte é aberta para receber a compra.
    [Fact]
    public async Task An_earlier_closing_sends_the_purchase_to_the_next_statement()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchase = (await s.Buy(4590, "2026-10-25")).Single();
        var installments = await s.Buy(9000, "2026-10-25", installments: 3);

        var edited = await s.Edited("2026-11", "2026-10-24", "2026-11-05");

        edited.MovedPurchases.ShouldBe(2);
        edited.TotalCents.ShouldBe(0);
        (await s.Statement("2026-12")).TotalCents.ShouldBe(4590 + 3000);
        (await s.Get(purchase.Id)).SettlementDate.ShouldBe(new DateOnly(2026, 12, 5));
        (await s.Get(installments[2].Id)).SettlementDate.ShouldBe(new DateOnly(2027, 2, 5));
    }

    // Caso 7 da tabela: a compra que a pessoa pôs na fatura fica.
    [Fact]
    public async Task A_pinned_purchase_stays_when_the_closing_changes()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var purchase = (await s.Buy(4590, "2026-10-27")).Single();
        await s.Moved(purchase.Id, "Previous");

        var edited = await s.Edited("2026-11", "2026-10-24", "2026-11-04");

        edited.MovedPurchases.ShouldBe(0);
        edited.TotalCents.ShouldBe(4590);
        var kept = await s.Get(purchase.Id);
        kept.StatementPinned.ShouldBeTrue();
        kept.SettlementDate.ShouldBe(new DateOnly(2026, 11, 4));
    }

    // Caso 8 da tabela (regra 5).
    [Fact]
    public async Task A_closing_that_crosses_a_neighbor_is_refused_and_nothing_changes()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(1000, "2026-09-20");
        var october = (await s.Buy(2000, "2026-10-20")).Single();

        await ShouldBeProblem(await s.EditDates("2026-11", "2026-09-26", "2026-11-05"), HttpStatusCode.BadRequest,
            "O fechamento tem de ser depois do fechamento da fatura anterior (26/09).");
        await ShouldBeProblem(await s.EditDates("2026-10", "2026-10-26", "2026-11-05"), HttpStatusCode.BadRequest,
            "O fechamento tem de ser antes do fechamento da fatura seguinte (26/10).");

        (await s.Statement("2026-11")).ClosingDate.ShouldBe(new DateOnly(2026, 10, 26));
        (await s.Get(october.Id)).SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
    }

    // Regra 6 na edição: a compra que a edição leva para uma fatura sendo paga não muda o valor pago.
    [Fact]
    public async Task Editing_dates_while_the_destination_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCard();
            await s.Buy(3000, "2026-10-25");
            await s.Buy(4000, "2026-11-10");
            var december = await s.Statement("2026-12");

            var responses = await Task.WhenAll(
                s.EditDates("2026-11", "2026-10-24", "2026-11-05"), s.Pay(december.Id, "2026-11-27"));

            responses[0].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
            responses[1].StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.IsSuccessStatusCode);

            december = await s.Statement("2026-12");
            if (december.IsPaid)
                (await s.In(december.Id)).Single(t => t.Type == "Transfer").AmountCents.ShouldBe(december.TotalCents);
            else
                december.TotalCents.ShouldBe(7000);
        }
    }

    // O giro a mais na roda do mês do iPhone, pela API: o vencimento que alcança o fechamento da fatura
    // seguinte é recusado, e o caixa de novembro não vai para dezembro em silêncio.
    [Fact]
    public async Task A_due_date_slip_into_the_next_cycle_is_refused_and_the_cash_view_stays()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(4000, "2026-10-10");
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(4000);

        await ShouldBeProblem(await s.EditDates("2026-11", "2026-10-26", "2026-12-04"), HttpStatusCode.BadRequest,
            "O vencimento tem de ser antes do fechamento da fatura seguinte (26/11).");

        (await s.Statement("2026-11")).DueDate.ShouldBe(new DateOnly(2026, 11, 5));
        (await s.Summary("2026-11")).ExpenseCents.ShouldBe(4000);
        (await s.Summary("2026-12")).ExpenseCents.ShouldBe(0);
    }

    // ---------- Prévia do ajuste de datas (etapa 2.20b) ----------

    // Regra 2 da seção 2.10: a prévia lista exatamente o que o salvar move, e não grava nada.
    [Fact]
    public async Task The_preview_lists_what_saving_moves_and_changes_nothing()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var snack = (await s.Buy(3000, "2026-10-25")).Single();
        var shoes = await s.Buy(9000, "2026-10-25", installments: 3);
        var stays = (await s.Buy(1000, "2026-10-20")).Single();
        await s.Buy(500, "2026-11-10"); // dezembro já existe: nada a abrir, e uma gravação indevida passaria

        var preview = await s.Previewed("2026-11", "2026-10-24", "2026-11-05");

        preview.MovedPurchases.Select(p => (p.TransactionId, p.AmountCents, p.InstallmentCount, p.FromReference, p.ToReference))
            .ShouldBe([(snack.Id, 3000L, 1, "2026-11", "2026-12"), (shoes[0].Id, 9000L, 3, "2026-11", "2026-12")], ignoreOrder: true);
        preview.MovedPurchases.ShouldAllBe(p => p.Description == "Lanche" && p.PurchaseDate == new DateOnly(2026, 10, 25) && p.Type == "Expense");
        (await s.Statement("2026-11")).ClosingDate.ShouldBe(new DateOnly(2026, 10, 26));
        (await s.Get(snack.Id)).SettlementDate.ShouldBe(new DateOnly(2026, 11, 5));
        (await s.Statement("2026-11")).TotalCents.ShouldBe(3000 + 3000 + 1000);

        var edited = await s.Edited("2026-11", "2026-10-24", "2026-11-05");

        edited.MovedPurchases.ShouldBe(preview.MovedPurchases.Count);
        var december = await s.Statement("2026-12");
        (await s.Get(snack.Id)).StatementId.ShouldBe(december.Id);
        (await s.Get(shoes[0].Id)).StatementId.ShouldBe(december.Id);
        (await s.Get(stays.Id)).StatementId.ShouldBe(edited.Id);
    }

    [Fact]
    public async Task The_preview_shows_purchases_coming_in()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var late = (await s.Buy(4590, "2026-10-27")).Single();
        await s.Buy(1000, "2026-10-20");

        var preview = await s.Previewed("2026-11", "2026-10-27", "2026-11-05");

        preview.MovedPurchases.Select(p => (p.TransactionId, p.FromReference, p.ToReference)).ShouldBe([(late.Id, "2026-12", "2026-11")]);
    }

    // A compra que a pessoa pôs na fatura fica de fora da prévia, como fica de fora do salvar.
    [Fact]
    public async Task The_preview_leaves_pinned_purchases_out()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var pinned = (await s.Buy(2000, "2026-10-27")).Single();
        await s.Moved(pinned.Id, "Previous");
        var free = (await s.Buy(1000, "2026-10-26")).Single();

        var preview = await s.Previewed("2026-11", "2026-10-25", "2026-11-05");

        preview.MovedPurchases.Select(p => p.TransactionId).ShouldBe([free.Id]);
    }

    [Fact]
    public async Task The_preview_refuses_like_saving_and_hides_other_users()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Buy(4000, "2026-10-10");
        await s.Buy(1000, "2026-09-10");

        await ShouldBeProblem(await s.Preview("2026-11", "2026-10-26", "2026-12-04"), HttpStatusCode.BadRequest,
            "O vencimento tem de ser antes do fechamento da fatura seguinte (26/11).");
        (await s.Pay((await s.Statement("2026-10")).Id, "2026-09-27")).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ShouldBeProblem(await s.Preview("2026-10", "2026-09-25", "2026-10-05"), HttpStatusCode.BadRequest,
            "Fatura paga não muda de datas. Desfaça o pagamento para ajustá-las.");

        using var stranger = await factory.CreateAuthenticatedClientAsync();
        var november = await s.Statement("2026-11");
        (await stranger.PostAsJsonAsync($"/statements/{november.Id}/date-preview",
            new { closingDate = "2026-10-24", dueDate = "2026-11-05" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
