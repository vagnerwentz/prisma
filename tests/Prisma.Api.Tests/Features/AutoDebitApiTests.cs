using System.Net;
using System.Net.Http.Json;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.15 (etapa 2.26, tarefa 4): a API do débito automático. Conta corrente Itaú. Horários em
// UTC: 09:00 em São Paulo é 12:00 UTC. Hoje é 20/10/2026 (terça).
[Collection(ApiCollection.Name)]
public sealed class AutoDebitApiTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(
        Guid Id, long AmountCents, DateOnly PurchaseDate, DateOnly SettlementDate, string Method, string Description,
        Guid? RecurrenceId, bool AmountEstimated);

    private sealed record RecurrenceDto(
        Guid Id, Guid AccountId, long AmountCents, string Description, string Method, string Frequency, DateOnly StartDate,
        DateOnly GeneratedThrough, DateOnly? NextOccurrence, string Kind, bool AmountVaries, DateOnly? NextTransactionDate);

    private sealed record SummaryDto(long ExpenseCents, long EstimatedExpenseCents);

    private sealed record CommittedItemDto(string Month, long ExpenseCents, long ProjectedExpenseCents);

    private sealed record CommittedDto(List<CommittedItemDto> Months);

    private sealed record ProblemDto(string? Detail);

    private static DateTime SaoPaulo(int day, int month, int year = 2026) => new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    private sealed class Scenario(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public Guid Checking { get; private set; }
        public Guid Visa { get; private set; }

        public async Task Setup()
        {
            Checking = await Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            Visa = await Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });
        }

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public Task<HttpResponseMessage> Post(
            long amount, string date, object? recurrence, Guid? account = null, string method = "Debit", string type = "Expense",
            string description = "Sabesp") =>
            Client.PostAsJsonAsync("/transactions", new
            {
                accountId = account ?? Checking, type, amountCents = amount, purchaseDate = date, method, description, recurrence,
            });

        // Sabesp: vence todo dia 15, R$ 95,00 médio, desde 15/09. Na criação, a de 15/10 já sai, a conferir.
        public async Task<Guid> Sabesp()
        {
            var response = await Post(9500, "2026-09-15", new { frequency = "Monthly", autoDebit = true, amountVaries = true });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await ToConfirm()).Single().Id;
        }

        public async Task<List<TransactionDto>> ToConfirm() =>
            (await Client.GetFromJsonAsync<List<TransactionDto>>("/transactions/to-confirm"))!;

        public async Task<List<RecurrenceDto>> Recurrences() =>
            (await Client.GetFromJsonAsync<List<RecurrenceDto>>("/recurrences"))!;

        public Task<HttpResponseMessage> Confirm(Guid id, long? amount = null) =>
            Client.PostAsJsonAsync($"/transactions/{id}/confirm-amount", new { amountCents = amount });

        public async Task<SummaryDto> Summary(string month) =>
            (await Client.GetFromJsonAsync<SummaryDto>($"/dashboard/summary?month={month}"))!;

        public Task<HttpResponseMessage> Patch(Guid id, long amount, string description = "Sabesp", string date = "2026-10-15") =>
            Client.PatchAsJsonAsync($"/transactions/{id}", new
            {
                accountId = Checking, type = "Expense", amountCents = amount, purchaseDate = date, categoryId = (Guid?)null,
                method = "Debit", description,
            });

        public Task<HttpResponseMessage> EditSeries(RecurrenceDto r, object? autoDebit = null, object? amountVaries = null, string? method = null) =>
            Client.PatchAsJsonAsync($"/recurrences/{r.Id}", new
            {
                accountId = r.AccountId, amountCents = r.AmountCents, categoryId = (Guid?)null, description = r.Description,
                method = method ?? r.Method, frequency = r.Frequency, nextDate = (string?)null, endDate = (string?)null,
                autoDebit, amountVaries,
            });

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start()
    {
        var factory = new PrismaApiFactory(postgres.ConnectionString, clock: new FakeClock(SaoPaulo(20, 10)));
        var s = new Scenario(await factory.CreateAuthenticatedClientAsync());
        await s.Setup();
        return (factory, s);
    }

    private static async Task ShouldBeRefused(HttpResponseMessage response, string message)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe(message);
    }

    // --- Criar ---

    [Fact]
    public async Task A_new_entry_starts_an_auto_debit_and_the_due_debit_waits_to_be_checked()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        await s.Sabesp();

        var series = (await s.Recurrences()).ShouldHaveSingleItem();
        (series.Kind, series.AmountVaries, series.Method).ShouldBe(("AutoDebit", true, "Debit"));
        (series.NextOccurrence, series.NextTransactionDate).ShouldBe((D(15, 11), D(16, 11))); // 15/11 é domingo

        var toConfirm = (await s.ToConfirm()).ShouldHaveSingleItem();
        (toConfirm.PurchaseDate, toConfirm.AmountCents, toConfirm.AmountEstimated, toConfirm.RecurrenceId)
            .ShouldBe((D(15, 10), 9500L, true, (Guid?)series.Id));
    }

    // A Copel de 20/09 (domingo), debitada em 21/09: a série parte do vencimento (regra 4).
    [Fact]
    public async Task An_existing_debit_becomes_an_auto_debit_from_its_due_date()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var response = await s.Post(18000, "2026-09-21", recurrence: null, description: "Copel");
        var copel = (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.Single().Id;

        var created = await s.Client.PostAsJsonAsync($"/transactions/{copel}/recurrence",
            new { frequency = "Monthly", autoDebit = true, amountVaries = true, dueDate = "2026-09-20" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = (await created.Content.ReadFromJsonAsync<RecurrenceDto>())!;
        (series.StartDate, series.GeneratedThrough, series.NextTransactionDate).ShouldBe((D(20, 9), D(20, 10), (DateOnly?)D(23, 11)));
        (await s.ToConfirm()).ShouldHaveSingleItem().PurchaseDate.ShouldBe(D(20, 10));
    }

    [Fact]
    public async Task What_the_rules_forbid_is_refused_and_nothing_is_saved()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        await ShouldBeRefused(await s.Post(9500, "2026-09-15", new { frequency = "Monthly", autoDebit = true }, account: s.Visa, method: "Credit"),
            "Débito automático só sai de conta corrente.");
        await ShouldBeRefused(await s.Post(500000, "2026-09-05", new { frequency = "Monthly", autoDebit = true }, type: "Income", method: "Ted"),
            "Débito automático é sempre despesa.");
        await ShouldBeRefused(await s.Post(9500, "2026-09-15", new { frequency = "Weekly", autoDebit = true }),
            "Débito automático é todo mês.");
        await ShouldBeRefused(await s.Post(9500, "2026-09-15", new { frequency = "Monthly", amountVaries = true }),
            "Só o débito automático tem valor que muda.");
        await ShouldBeRefused(await s.Post(9500, "2026-09-15", new { frequency = "Monthly", dueDate = "2026-09-14" }),
            "Só o débito automático tem vencimento.");
        await ShouldBeRefused(await s.Post(18000, "2026-09-21", new { frequency = "Monthly", autoDebit = true, dueDate = "2026-09-13" }),
            "O vencimento fica até 7 dias antes da data do lançamento.");

        (await s.Client.GetFromJsonAsync<List<TransactionDto>>("/transactions?from=2026-09-01&to=2026-10-31"))!.ShouldBeEmpty();
        (await s.Recurrences()).ShouldBeEmpty();
    }

    // --- Conferir (regras 6 a 8) ---

    [Fact]
    public async Task Confirming_the_real_amount_leaves_the_bell_and_fixes_the_month()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var october = await s.Sabesp();
        (await s.Summary("2026-10")).ShouldBe(new SummaryDto(9500, 9500));
        (await s.Summary("2026-09")).ShouldBe(new SummaryDto(9500, 0)); // o primeiro, digitado, e só o mês dele

        var confirmed = await s.Confirm(october, 10237);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await confirmed.Content.ReadFromJsonAsync<TransactionDto>())!;
        (body.AmountCents, body.AmountEstimated).ShouldBe((10237L, false));
        (await s.ToConfirm()).ShouldBeEmpty();
        (await s.Summary("2026-10")).ShouldBe(new SummaryDto(10237, 0));
        (await s.Recurrences()).Single().AmountCents.ShouldBe(9500); // a estimativa da série não muda

        await ShouldBeRefused(await s.Confirm(october, 11000), "Este lançamento já foi conferido.");
    }

    [Fact]
    public async Task Confirming_without_an_amount_keeps_the_estimate()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var october = await s.Sabesp();

        (await s.Confirm(october)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await s.ToConfirm()).ShouldBeEmpty();
        (await s.Summary("2026-10")).ShouldBe(new SummaryDto(9500, 0));
    }

    [Fact]
    public async Task Confirming_needs_a_positive_amount()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var october = await s.Sabesp();

        await ShouldBeRefused(await s.Confirm(october, 0), "O valor deve ser maior que zero.");
        (await s.ToConfirm()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Editing_the_amount_confirms_and_editing_the_description_does_not()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var october = await s.Sabesp();

        (await s.Patch(october, 9500, description: "Sabesp casa")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.ToConfirm()).ShouldHaveSingleItem();

        (await s.Patch(october, 10237, description: "Sabesp casa")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.ToConfirm()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_leaves_the_bell_and_restoring_brings_it_back()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        var october = await s.Sabesp();

        (await s.Client.DeleteAsync($"/transactions/{october}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.ToConfirm()).ShouldBeEmpty();
        (await s.Summary("2026-10")).EstimatedExpenseCents.ShouldBe(0);

        (await s.Client.PostAsync($"/transactions/{october}/restore", null)).IsSuccessStatusCode.ShouldBeTrue();
        (await s.ToConfirm()).ShouldHaveSingleItem().AmountEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Nobody_sees_or_confirms_the_debits_of_someone_else()
    {
        var (factory, a) = await Start();
        await using var _ = factory;
        using var __ = a;
        var october = await a.Sabesp();
        using var b = new Scenario(await factory.CreateAuthenticatedClientAsync());
        await b.Setup();

        (await b.ToConfirm()).ShouldBeEmpty();
        (await b.Summary("2026-10")).ShouldBe(new SummaryDto(0, 0));
        (await b.Confirm(october, 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await a.ToConfirm()).ShouldHaveSingleItem().AmountCents.ShouldBe(9500);
    }

    // Um mês sem conferir não impede o seguinte (regra 7): o sino mostra os dois, o mais antigo primeiro.
    [Fact]
    public async Task The_bell_lists_the_oldest_first()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;

        (await s.Post(9500, "2026-08-15", new { frequency = "Monthly", autoDebit = true, amountVaries = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await s.ToConfirm()).Select(t => t.PurchaseDate).ShouldBe([D(15, 9), D(15, 10)]);
    }

    // A tabela "Previsão" (regra 9): hoje 20/10/2026, com Sabesp (desde 15/09), Copel (desde 20/10) e Internet de
    // valor fixo (desde 31/08). Cada débito previsto conta no mês em que é debitado: novembro tem a Internet duas
    // vezes (a de 31/10, debitada em 03/11, e a de 30/11).
    [Fact]
    public async Task The_forecast_counts_each_debit_in_the_month_it_is_debited()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Sabesp();
        (await s.Post(18000, "2026-10-20", new { frequency = "Monthly", autoDebit = true, amountVaries = true }, description: "Copel"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await s.Post(12000, "2026-08-31", new { frequency = "Monthly", autoDebit = true, amountVaries = false }, description: "Internet"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var committed = (await s.Client.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!.Months;

        committed[0].ShouldBe(new CommittedItemDto("2026-11", 0, 51500));
        // Dezembro: Sabesp 15/12, Copel de 20/12 (domingo) em 21/12, Internet 31/12.
        committed[1].ShouldBe(new CommittedItemDto("2026-12", 0, 39500));
    }

    // --- Editar a série (regra 10) ---

    // A tela aberta antes da 2.26 não manda o tipo: a série fica como está (CLAUDE.md, seção 6).
    [Fact]
    public async Task Editing_without_the_new_fields_keeps_the_auto_debit()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Sabesp();
        var series = (await s.Recurrences()).Single();

        (await s.EditSeries(series with { AmountCents = 11000 }, method: "Pix")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var edited = (await s.Recurrences()).Single();
        (edited.Kind, edited.AmountVaries, edited.Method, edited.AmountCents).ShouldBe(("AutoDebit", true, "Debit", 11000L));
    }

    [Fact]
    public async Task A_series_stops_being_an_auto_debit()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        await s.Sabesp();
        var series = (await s.Recurrences()).Single();

        (await s.EditSeries(series, autoDebit: false, method: "Pix")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var edited = (await s.Recurrences()).Single();
        (edited.Kind, edited.AmountVaries, edited.Method, edited.NextTransactionDate).ShouldBe(("Regular", false, "Pix", (DateOnly?)D(15, 11)));
        await ShouldBeRefused(await s.EditSeries(edited, amountVaries: true), "Só o débito automático tem valor que muda.");
    }

    [Fact]
    public async Task A_regular_series_becomes_an_auto_debit_with_a_varying_amount()
    {
        var (factory, s) = await Start();
        await using var _ = factory;
        using var __ = s;
        (await s.Post(9500, "2026-09-15", new { frequency = "Monthly" }, method: "Pix")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = (await s.Recurrences()).Single();

        (await s.EditSeries(series, autoDebit: true, amountVaries: true)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var edited = (await s.Recurrences()).Single();
        (edited.Kind, edited.AmountVaries, edited.Method).ShouldBe(("AutoDebit", true, "Debit"));
    }
}
