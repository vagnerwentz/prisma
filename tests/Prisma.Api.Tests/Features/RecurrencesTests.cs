using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Recurrences;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.14 (etapa 2.25, tarefa 5a): a API das séries. Visa "fecha 26, vence 5"; conta corrente
// Itaú. Horários em UTC: 09:00 em São Paulo é 12:00 UTC.
[Collection(ApiCollection.Name)]
public sealed class RecurrencesTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id, long AmountCents, Guid? RecurrenceId, Guid? StatementId = null,
        DateOnly? PurchaseDate = null, bool StatementPinned = false, Guid? AccountId = null, string? Type = null);

    private sealed record PendingDto(Guid Id, Guid AccountId, long AmountCents, DateOnly OccurrenceDate, string StatementReference);

    private sealed record StatementDto(Guid Id, string Reference, bool IsPaid, long TotalCents, long ProjectedCents = 0);

    private sealed record CommittedItemDto(string Month, long ExpenseCents, long ProjectedExpenseCents);

    private sealed record CommittedDto(List<CommittedItemDto> Months);

    private sealed record SummaryDto(long ExpenseCents);

    private sealed record BalanceDto(Guid AccountId, long? BalanceCents, long? OwedCents);

    private sealed record RecurrenceDto(
        Guid Id, Guid AccountId, string Type, long AmountCents, Guid? CategoryId, string Description, string Method,
        string Frequency, DateOnly StartDate, DateOnly? EndDate, DateOnly GeneratedThrough, DateOnly? NextOccurrence,
        bool IsEnded, List<PendingDto> Pendings);

    private sealed record ProblemDto(string? Detail);

    private static DateTime SaoPaulo(int day, int month, int year = 2026) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario(HttpClient client, FakeClock clock) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public FakeClock Clock { get; } = clock;
        public Guid Checking { get; private set; }
        public Guid Visa { get; private set; }

        public async Task Setup()
        {
            Checking = await Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            Visa = await Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });
        }

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public Task<HttpResponseMessage> Post(Guid account, long amount, string date, string method = "Credit",
            object? recurrence = null, string type = "Expense", int installments = 1, string description = "Pet") =>
            Client.PostAsJsonAsync("/transactions", new
            {
                accountId = account, type, amountCents = amount, purchaseDate = date, method, description, installments,
                recurrence,
            });

        public async Task<Guid> Buy(Guid account, long amount, string date, string method = "Credit", string description = "Pet")
        {
            var response = await Post(account, amount, date, method, description: description);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.Single().Id;
        }

        public Task<HttpResponseMessage> Repeat(Guid transaction, string frequency = "Monthly", string? endDate = null) =>
            Client.PostAsJsonAsync($"/transactions/{transaction}/recurrence", new { frequency, endDate });

        public async Task<RecurrenceDto> RepeatOk(Guid transaction, string frequency = "Monthly")
        {
            var response = await Repeat(transaction, frequency);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<RecurrenceDto>())!;
        }

        public Task<HttpResponseMessage> Edit(RecurrenceDto r, long? amount = null, Guid? account = null, string? method = null,
            string? frequency = null, string? nextDate = null, string? endDate = null) =>
            Client.PatchAsJsonAsync($"/recurrences/{r.Id}", new
            {
                accountId = account ?? r.AccountId, amountCents = amount ?? r.AmountCents, categoryId = r.CategoryId,
                description = r.Description, method = method ?? r.Method, frequency = frequency ?? r.Frequency, nextDate,
                endDate = endDate ?? r.EndDate?.ToString("yyyy-MM-dd"),
            });

        public Task<HttpResponseMessage> End(Guid recurrence) =>
            Client.PostAsync($"/recurrences/{recurrence}/end", null);

        public Task<HttpResponseMessage> Pay(Guid card, string reference, string date) =>
            Statement(card, reference).ContinueWith(s => Client.PostAsJsonAsync($"/statements/{s.Result.Id}/pay",
                new { fromAccountId = Checking, date, method = "Boleto" })).Unwrap();

        public async Task<StatementDto> Statement(Guid card, string reference) =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{card}/statements"))!.Single(st => st.Reference == reference);

        public Task<HttpResponseMessage> Launch(Guid pending, string where) =>
            Client.PostAsJsonAsync($"/recurrences/pendings/{pending}/launch", new { where });

        public Task<HttpResponseMessage> Discard(Guid pending) =>
            Client.DeleteAsync($"/recurrences/pendings/{pending}");

        public async Task<Guid> NewCard(string name) =>
            Visa = await Account(new { name, type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });

        public async Task<List<RecurrenceDto>> List() =>
            (await Client.GetFromJsonAsync<List<RecurrenceDto>>("/recurrences"))!;

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start(DateTime utcNow)
    {
        var clock = new FakeClock(utcNow);
        var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        var s = new Scenario(await factory.CreateAuthenticatedClientAsync(), clock);
        await s.Setup();
        return (factory, s);
    }

    private async Task<T> Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<long> Generated(Guid recurrence) =>
        Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r", ("r", recurrence));

    private Task<long> AmountOn(Guid recurrence, string occurrence) =>
        Scalar<long>("SELECT amount_cents FROM transactions WHERE recurrence_id = @r AND occurrence_date = @d",
            ("r", recurrence), ("d", DateOnly.Parse(occurrence)));

    private static async Task ShouldFail(HttpResponseMessage response, HttpStatusCode status, string message)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Detail.ShouldBe(message);
    }

    // O caso do pai: a cobrança de 25/09 já lançada vira a primeira ocorrência (regra 1).
    [Fact]
    public async Task A_charge_already_made_starts_a_series_and_generates_what_is_due()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var first = await s.Buy(s.Visa, 40000, "2026-09-25");

        var series = await s.RepeatOk(first);

        series.StartDate.ShouldBe(new DateOnly(2026, 9, 25));
        series.GeneratedThrough.ShouldBe(new DateOnly(2026, 10, 25)); // gerou na hora, sem esperar a tarefa
        series.NextOccurrence.ShouldBe(new DateOnly(2026, 11, 25));
        (await Generated(series.Id)).ShouldBe(2);
        (await s.Client.GetFromJsonAsync<TransactionDto>($"/transactions/{first}"))!.RecurrenceId.ShouldBe(series.Id);
        await ShouldFail(await s.Repeat(first), HttpStatusCode.Conflict, "Este lançamento já faz parte de uma série.");
    }

    // O "Novo lançamento" com "Toda semana": a diarista de 02/10, lançada em 16/10.
    [Fact]
    public async Task A_new_transaction_can_start_a_weekly_series()
    {
        var (factory, s) = await Start(SaoPaulo(16, 10));
        await using var _ = factory;
        using var __ = s;

        var response = await s.Post(s.Checking, 50000, "2026-10-02", "Pix", new { frequency = "Weekly" }, description: "Diarista");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.Single();
        created.RecurrenceId.ShouldNotBeNull();
        (await Generated(created.RecurrenceId!.Value)).ShouldBe(3); // 02/10, 09/10 e 16/10
        (await s.List()).Single().NextOccurrence.ShouldBe(new DateOnly(2026, 10, 23));
    }

    [Fact]
    public async Task What_does_not_repeat_is_refused_and_nothing_is_saved()
    {
        var (factory, s) = await Start(SaoPaulo(16, 10));
        await using var _ = factory;
        using var __ = s;
        var monthly = new { frequency = "Monthly" };

        await ShouldFail(await s.Post(s.Visa, 30000, "2026-10-10", recurrence: monthly, installments: 3),
            HttpStatusCode.BadRequest, "Compra parcelada não se repete.");
        await ShouldFail(await s.Post(s.Visa, 3000, "2026-10-10", recurrence: monthly, type: "Refund"),
            HttpStatusCode.BadRequest, "Estorno não se repete.");
        await ShouldFail(await s.Post(s.Checking, 3000, "2026-10-10", "Pix", new { frequency = "Monthly", endDate = "2026-10-10" }),
            HttpStatusCode.BadRequest, "O término deve ser depois do primeiro lançamento (10/10/2026).");
        (await s.Client.GetFromJsonAsync<List<TransactionDto>>("/transactions?from=2026-10-01&to=2026-10-31"))!.ShouldBeEmpty();

        var transfer = await s.Client.PostAsJsonAsync("/transfers", new
        {
            fromAccountId = s.Checking, toAccountId = await Account(s, "Poupança"), amountCents = 1000, date = "2026-10-10", method = "Pix",
        });
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created);
        var leg = (await s.Client.GetFromJsonAsync<List<TransactionDto>>("/transactions?from=2026-10-01&to=2026-10-31"))!.First();
        await ShouldFail(await s.Repeat(leg.Id), HttpStatusCode.BadRequest, "Transferência entre contas não se repete.");
        (await s.List()).ShouldBeEmpty();
    }

    private static async Task<Guid> Account(Scenario s, string name) =>
        (await (await s.Client.PostAsJsonAsync("/accounts", new { name, type = "Checking", initialBalanceCents = 0 }))
            .Content.ReadFromJsonAsync<IdDto>())!.Id;

    // Regra 6: vale do próximo em diante. O de 25/10 continua R$ 400,00.
    [Fact]
    public async Task Editing_changes_only_what_comes_next()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        s.Clock.UtcNow = SaoPaulo(1, 11);

        var edited = await s.Edit(series, amount: 45000);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await edited.Content.ReadFromJsonAsync<RecurrenceDto>())!.AmountCents.ShouldBe(45000);
        s.Clock.UtcNow = SaoPaulo(25, 11);
        await factory.Services.GetRequiredService<RecurrenceRunner>()
            .RunAsync(CancellationToken.None);
        (await AmountOn(series.Id, "2026-10-25")).ShouldBe(40000);
        (await AmountOn(series.Id, "2026-11-25")).ShouldBe(45000);
    }

    // Mudar o dia do mês: a próxima data vira a nova partida, e a ocorrência de hoje já sai.
    [Fact]
    public async Task A_new_next_date_is_generated_right_away_when_it_is_today()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Checking, 100000, "2026-09-10", "Pix"));
        s.Clock.UtcNow = SaoPaulo(30, 10);

        var edited = await s.Edit(series, frequency: "Weekly", nextDate: "2026-10-30");

        (await edited.Content.ReadFromJsonAsync<RecurrenceDto>())!.NextOccurrence.ShouldBe(new DateOnly(2026, 11, 6));
        (await AmountOn(series.Id, "2026-10-30")).ShouldBe(100000);
    }

    [Fact]
    public async Task Edits_that_break_the_series_are_refused()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));

        await ShouldFail(await s.Edit(series, frequency: "Weekly"), HttpStatusCode.BadRequest,
            "Informe a próxima data da nova frequência.");
        await ShouldFail(await s.Edit(series, nextDate: "2026-10-25"), HttpStatusCode.BadRequest,
            "A próxima data tem de ser depois do último lançamento (25/10/2026).");
        await ShouldFail(await s.Edit(series, endDate: "2026-10-24"), HttpStatusCode.BadRequest,
            "O término não pode ser antes do último lançamento (25/10/2026).");
        await ShouldFail(await s.Edit(series, account: s.Checking, method: "Pix"), HttpStatusCode.BadRequest,
            "A série continua no mesmo tipo de conta: cartão por cartão, conta por conta.");
        await ShouldFail(await s.Edit(series, amount: 0), HttpStatusCode.BadRequest, "O valor deve ser maior que zero.");
    }

    // Regra 7: encerrar põe o término no último gerado. O de 25/10 fica; nada depois.
    [Fact]
    public async Task Ending_keeps_the_history_and_generates_nothing_more()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        s.Clock.UtcNow = SaoPaulo(1, 11);

        var ended = await s.End(series.Id);

        ended.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await ended.Content.ReadFromJsonAsync<RecurrenceDto>())!;
        (dto.EndDate, dto.NextOccurrence, dto.IsEnded).ShouldBe((new DateOnly(2026, 10, 25), (DateOnly?)null, true));
        s.Clock.UtcNow = SaoPaulo(25, 11);
        await factory.Services.GetRequiredService<RecurrenceRunner>()
            .RunAsync(CancellationToken.None);
        (await Generated(series.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task A_user_never_sees_nor_changes_the_series_of_another()
    {
        var (factory, a) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = a;
        using var b = new Scenario(await factory.CreateAuthenticatedClientAsync(), a.Clock);
        await b.Setup();
        var first = await a.Buy(a.Visa, 40000, "2026-09-25");
        var series = await a.RepeatOk(first);

        (await b.List()).ShouldBeEmpty();
        (await b.Repeat(first)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Edit(series, account: b.Visa)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.End(series.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldFail(await a.Edit(series, account: b.Visa), HttpStatusCode.BadRequest, "Conta não encontrada.");
    }

    // Dois toques em "Se repete todo mês": uma série só. Sem a trava, as duas gerariam o pet em dobro.
    [Fact]
    public async Task Two_taps_at_once_start_one_series()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 8; round++)
        {
            var first = await s.Buy(s.Visa, 40000, "2026-09-25", description: $"Pet {round}");

            var taps = await Task.WhenAll(s.Repeat(first), s.Repeat(first));

            taps.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            taps.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
            (await Scalar<long>("SELECT count(*) FROM recurrences WHERE description = @d", ("d", $"Pet {round}"))).ShouldBe(1);
            (await Scalar<long>("SELECT count(*) FROM transactions WHERE description = @d AND deleted_at IS NULL", ("d", $"Pet {round}")))
                .ShouldBe(2); // 25/09 e 25/10
        }
    }

    // Regra 10: encerrar enquanto a tarefa gera. Na ordem que for, nada fica depois do término. Sem o xmin
    // da série, os dois gravariam colunas diferentes e passariam: término em 02/10 e lançamentos depois dele.
    [Fact]
    public async Task Ending_while_generating_never_leaves_an_occurrence_after_the_end()
    {
        var (factory, s) = await Start(SaoPaulo(30, 10));
        await using var _ = factory;
        using var __ = s;
        var runner = factory.Services.GetRequiredService<RecurrenceRunner>();
        var userId = (await s.Client.GetFromJsonAsync<IdDto>("/auth/me"))!.Id;

        for (var round = 0; round < 8; round++)
        {
            // Criada direto no banco, já atrasada, para a geração e o encerramento disputarem a mesma versão.
            var first = await s.Buy(s.Checking, 50000, "2026-01-02", "Pix", $"Diarista {round}");
            var id = Guid.NewGuid();
            await Scalar<int>("""
                INSERT INTO recurrences (id, user_id, account_id, type, amount_cents, description, method, frequency,
                    start_date, generated_through, created_at, updated_at)
                SELECT @id, user_id, account_id, type, amount_cents, description, method, 'Weekly', purchase_date,
                    purchase_date, now(), now() FROM transactions WHERE id = @t;
                UPDATE transactions SET recurrence_id = @id, occurrence_date = purchase_date WHERE id = @t
                RETURNING 0
                """, ("id", id), ("t", first));

            var ended = s.End(id);
            await Task.WhenAll(runner.RunForUserAsync(userId, CancellationToken.None), ended);
            (await ended).StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
            if ((await ended).StatusCode == HttpStatusCode.Conflict)
                (await s.End(id)).StatusCode.ShouldBe(HttpStatusCode.OK);

            (await Scalar<long>("""
                SELECT count(*) FROM transactions t JOIN recurrences r ON r.id = t.recurrence_id
                WHERE r.id = @id AND t.occurrence_date > r.end_date
                """, ("id", id))).ShouldBe(0);
        }
    }

    // --- Pendências (regra 9) ---

    // A tabela da 2.14: a fatura 2026-11 do Visa foi paga em 27/10, e a série do pet só nasceu em 28/10, a
    // partir da cobrança de 25/09. A de 25/10 cairia na fatura paga: fica pendente.
    private static async Task<(RecurrenceDto Series, PendingDto Pending)> PendingPet(Scenario s)
    {
        var first = await s.Buy(s.Visa, 40000, "2026-09-25");
        await s.Buy(s.Visa, 5000, "2026-10-20", description: "Ração");
        (await s.Pay(s.Visa, "2026-11", "2026-10-27")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = await s.RepeatOk(first);
        return (series, series.Pendings.ShouldHaveSingleItem());
    }

    private Task<Guid> PaymentOf(Guid statement) =>
        Scalar<Guid>("SELECT id FROM transactions WHERE statement_id = @s AND type = 'Transfer' AND deleted_at IS NULL",
            ("s", statement));

    [Fact]
    public async Task A_pending_charge_is_launched_pinned_in_the_next_statement()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = s;
        var (series, pending) = await PendingPet(s);
        (pending.OccurrenceDate, pending.StatementReference, pending.AmountCents, pending.AccountId)
            .ShouldBe((new DateOnly(2026, 10, 25), "2026-11", 40000L, s.Visa));

        var launched = await s.Launch(pending.Id, "NextStatement");

        launched.StatusCode.ShouldBe(HttpStatusCode.Created);
        var charge = (await launched.Content.ReadFromJsonAsync<TransactionDto>())!;
        var december = await s.Statement(s.Visa, "2026-12");
        (charge.StatementId, charge.StatementPinned, charge.PurchaseDate, charge.RecurrenceId)
            .ShouldBe(((Guid?)december.Id, true, (DateOnly?)new DateOnly(2026, 10, 25), (Guid?)series.Id));
        december.TotalCents.ShouldBe(40000);
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(5000); // a paga não mudou
        (await s.List()).Single().Pendings.ShouldBeEmpty();
        await ShouldFail(await s.Launch(pending.Id, "NextStatement"), HttpStatusCode.NotFound, "Pendência não encontrada.");
    }

    [Fact]
    public async Task A_pending_charge_waits_for_the_payment_to_be_undone()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = s;
        var (_, pending) = await PendingPet(s);

        await ShouldFail(await s.Launch(pending.Id, "SameStatement"), HttpStatusCode.BadRequest,
            "A fatura desta cobrança continua paga. Desfaça o pagamento ou lance na fatura seguinte.");
        var november = await s.Statement(s.Visa, "2026-11");
        (await s.Client.DeleteAsync($"/transactions/{await PaymentOf(november.Id)}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var launched = await s.Launch(pending.Id, "SameStatement");

        launched.StatusCode.ShouldBe(HttpStatusCode.Created);
        var charge = (await launched.Content.ReadFromJsonAsync<TransactionDto>())!;
        (charge.StatementId, charge.StatementPinned).ShouldBe(((Guid?)november.Id, false));
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(45000);
    }

    [Fact]
    public async Task A_discarded_pending_never_comes_back()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = s;
        var (series, pending) = await PendingPet(s);

        (await s.Discard(pending.Id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Scalar<int>("UPDATE recurrences SET generated_through = '2026-09-25' WHERE id = @r RETURNING 0", ("r", series.Id));
        await factory.Services.GetRequiredService<RecurrenceRunner>().RunAsync(CancellationToken.None);

        (await s.List()).Single().Pendings.ShouldBeEmpty();
        (await Generated(series.Id)).ShouldBe(1);
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(5000);
    }

    [Fact]
    public async Task Another_user_cannot_resolve_my_pending()
    {
        var (factory, a) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = a;
        using var b = new Scenario(await factory.CreateAuthenticatedClientAsync(), a.Clock);
        await b.Setup();
        var (_, pending) = await PendingPet(a);

        (await b.Launch(pending.Id, "NextStatement")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Discard(pending.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.List()).Single().Pendings.ShouldHaveSingleItem();
    }

    // Dois toques em "Lançar": uma cobrança só. A fatura de dezembro já existe: a trava dela (StatementTouch)
    // segura o segundo toque, e o índice da ocorrência é a segunda trava.
    [Fact]
    public async Task Two_taps_on_launch_create_one_charge()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 6; round++)
        {
            await s.NewCard($"Visa {round}");
            var (series, pending) = await PendingPet(s);
            await s.Buy(s.Visa, 1000, "2026-10-27", description: "Farmácia"); // abre a fatura de dezembro

            var taps = await Task.WhenAll(s.Launch(pending.Id, "NextStatement"), s.Launch(pending.Id, "NextStatement"));

            taps.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            taps.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound).ShouldBe(1);
            (await Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'",
                ("r", series.Id))).ShouldBe(1);
        }
    }

    // Lançar na fatura de novembro, com o pagamento desfeito, enquanto a pessoa a paga de novo: a fatura paga
    // nunca fica com valor diferente do pagamento (StatementTouch).
    [Fact]
    public async Task Launching_while_the_statement_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 6; round++)
        {
            await s.NewCard($"Master {round}");
            var (_, pending) = await PendingPet(s);
            var november = await s.Statement(s.Visa, "2026-11");
            (await s.Client.DeleteAsync($"/transactions/{await PaymentOf(november.Id)}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await Task.WhenAll(s.Launch(pending.Id, "SameStatement"), s.Pay(s.Visa, "2026-11", "2026-10-27"));

            november = await s.Statement(s.Visa, "2026-11");
            if (november.IsPaid)
                (await Scalar<long>("SELECT amount_cents FROM transactions WHERE statement_id = @s AND type = 'Transfer' AND deleted_at IS NULL",
                    ("s", november.Id))).ShouldBe(november.TotalCents);
        }
    }

    // --- Previsão (regra 11): a tabela "Previsão" da 2.14, hoje 20/10 ---

    private async Task<(PrismaApiFactory, Scenario)> PetAndRentOn20October()
    {
        var (factory, s) = await Start(SaoPaulo(20, 10));
        await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        await s.RepeatOk(await s.Buy(s.Checking, 100000, "2026-09-10", "Pix", "Aluguel")); // o de 10/10 sai na hora
        await s.Buy(s.Visa, 5000, "2026-10-20", description: "Farmácia"); // abre a fatura 2026-11, a aberta
        return (factory, s);
    }

    [Fact]
    public async Task The_months_ahead_show_what_the_series_will_charge_apart_from_what_exists()
    {
        var (factory, s) = await PetAndRentOn20October();
        await using var _ = factory;
        using var __ = s;

        var committed = (await s.Client.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!.Months;

        committed[0].ShouldBe(new CommittedItemDto("2026-11", 5000, 140000)); // a farmácia existe; pet e aluguel, previstos
        committed[1].ShouldBe(new CommittedItemDto("2026-12", 0, 140000));
        var november = await s.Statement(s.Visa, "2026-11");
        (november.TotalCents, november.ProjectedCents).ShouldBe((5000L, 40000L));
    }

    // O Resumo e os saldos só mostram o que aconteceu (CLAUDE.md, 7.1).
    [Fact]
    public async Task The_summary_and_the_balances_do_not_count_the_projection()
    {
        var (factory, s) = await PetAndRentOn20October();
        await using var _ = factory;
        using var __ = s;

        (await s.Client.GetFromJsonAsync<SummaryDto>("/dashboard/summary?month=2026-11"))!.ExpenseCents.ShouldBe(5000);
        var balances = (await s.Client.GetFromJsonAsync<List<BalanceDto>>("/accounts/balances"))!;
        balances.Single(b => b.AccountId == s.Checking).BalanceCents.ShouldBe(-200000); // 10/09 e 10/10, não 10/11
        balances.Single(b => b.AccountId == s.Visa).OwedCents.ShouldBe(45000); // pet de 25/09 e farmácia, sem o pet de 25/10
    }

    // Nada contado duas vezes: em 25/10, gerado o pet, ele está no total da fatura e fora da previsão.
    [Fact]
    public async Task Once_generated_the_projection_moves_into_the_total()
    {
        var (factory, s) = await PetAndRentOn20October();
        await using var _ = factory;
        using var __ = s;
        s.Clock.UtcNow = SaoPaulo(25, 10);
        await factory.Services.GetRequiredService<RecurrenceRunner>().RunAsync(CancellationToken.None);

        var november = await s.Statement(s.Visa, "2026-11");
        (november.TotalCents, november.ProjectedCents).ShouldBe((45000L, 0L));
        var committed = (await s.Client.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!.Months;
        committed[0].ShouldBe(new CommittedItemDto("2026-11", 45000, 100000)); // só o aluguel de 10/11 previsto
    }

    // --- Lacunas fechadas antes do commit (a tabela "Geração" da 2.14 e a troca de cartão) ---

    private Task<Guid> OccurrenceId(Guid recurrence, string occurrence) =>
        Scalar<Guid>("SELECT id FROM transactions WHERE recurrence_id = @r AND occurrence_date = @d",
            ("r", recurrence), ("d", DateOnly.Parse(occurrence)));

    // Só o usuário do teste: o banco dos testes é compartilhado, e RunAsync contaria as séries dos outros.
    private static async Task<RecurrenceRunSummary> Run(PrismaApiFactory factory, Scenario s)
    {
        var userId = (await s.Client.GetFromJsonAsync<IdDto>("/auth/me"))!.Id;
        return await factory.Services.GetRequiredService<RecurrenceRunner>().RunForUserAsync(userId, CancellationToken.None);
    }

    // "A pessoa muda a data do pet de 25/10 para 26/10: continua ligado à série; nada é gerado de novo."
    [Fact]
    public async Task Changing_the_date_of_a_generated_charge_keeps_it_in_the_series()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        var pet = await OccurrenceId(series.Id, "2026-10-25");

        (await s.Client.PatchAsJsonAsync($"/transactions/{pet}", new
        {
            accountId = s.Visa, type = "Expense", amountCents = 40000, purchaseDate = "2026-10-26", categoryId = (Guid?)null,
            method = "Credit", description = "Pet",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        s.Clock.UtcNow = SaoPaulo(26, 10);

        (await Run(factory, s)).Created.ShouldBe(0);
        (await Scalar<Guid>("SELECT recurrence_id FROM transactions WHERE id = @t", ("t", pet))).ShouldBe(series.Id);
        (await Scalar<DateOnly>("SELECT occurrence_date FROM transactions WHERE id = @t", ("t", pet))).ShouldBe(new DateOnly(2026, 10, 25));
        (await Scalar<DateOnly>("SELECT purchase_date FROM transactions WHERE id = @t", ("t", pet))).ShouldBe(new DateOnly(2026, 10, 26));
        (await Generated(series.Id)).ShouldBe(2);
    }

    // "A pessoa exclui o pet de 25/10: nenhuma execução o recria; restaurar o traz de volta."
    [Fact]
    public async Task A_deleted_generated_charge_comes_back_only_by_restoring_it()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        var pet = await OccurrenceId(series.Id, "2026-10-25");
        (await s.Client.DeleteAsync($"/transactions/{pet}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.PostAsync($"/transactions/{pet}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Run(factory, s)).Created.ShouldBe(0);

        (await Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r AND deleted_at IS NULL", ("r", series.Id)))
            .ShouldBe(2);
        (await Scalar<Guid>("SELECT recurrence_id FROM transactions WHERE id = @t", ("t", pet))).ShouldBe(series.Id);
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(40000);
    }

    // Regra 6: a conta muda de cartão para cartão. A próxima cobrança sai no cartão novo, na fatura dele
    // (Master "fecha 5, vence 12": a compra de 25/11 cai na fatura que fecha em 05/12 e vence em 12/12).
    [Fact]
    public async Task Moving_the_series_to_another_card_charges_the_next_one_there()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10));
        await using var _ = factory;
        using var __ = s;
        var master = (await (await s.Client.PostAsJsonAsync("/accounts", new
        {
            name = "Master", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12,
        })).Content.ReadFromJsonAsync<IdDto>())!.Id;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        s.Clock.UtcNow = SaoPaulo(1, 11);

        (await s.Edit(series, account: master)).StatusCode.ShouldBe(HttpStatusCode.OK);
        s.Clock.UtcNow = SaoPaulo(25, 11);
        await Run(factory, s);

        var november = await OccurrenceId(series.Id, "2026-11-25");
        (await Scalar<Guid>("SELECT account_id FROM transactions WHERE id = @t", ("t", november))).ShouldBe(master);
        (await Scalar<DateOnly>("SELECT settlement_date FROM transactions WHERE id = @t", ("t", november))).ShouldBe(new DateOnly(2026, 12, 12));
        (await s.Statement(master, "2026-12")).TotalCents.ShouldBe(40000);
        // A de 25/10 ficou no Visa, como foi gerada.
        (await Scalar<Guid>("SELECT account_id FROM transactions WHERE id = @t", ("t", await OccurrenceId(series.Id, "2026-10-25"))))
            .ShouldBe(s.Visa);
    }

    // A previsão respeita as datas editadas da fatura, como a geração (2.9): com a fatura 2026-11 fechando
    // em 24/10, o pet de 25/10 cai na de dezembro, na previsão e depois de gerado.
    [Fact]
    public async Task The_projection_follows_edited_statement_dates_like_the_generation()
    {
        var (factory, s) = await Start(SaoPaulo(20, 10));
        await using var _ = factory;
        using var __ = s;
        var series = await s.RepeatOk(await s.Buy(s.Visa, 40000, "2026-09-25"));
        await s.Buy(s.Visa, 5000, "2026-10-20", description: "Farmácia"); // abre a fatura 2026-11
        var november = await s.Statement(s.Visa, "2026-11");
        (await s.Client.PatchAsJsonAsync($"/statements/{november.Id}", new { closingDate = "2026-10-24", dueDate = "2026-11-05" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await s.Statement(s.Visa, "2026-11")).ProjectedCents.ShouldBe(0);
        var committed = (await s.Client.GetFromJsonAsync<CommittedDto>("/dashboard/committed"))!.Months;
        committed[0].ProjectedExpenseCents.ShouldBe(0); // novembro
        committed[1].ProjectedExpenseCents.ShouldBe(80000); // dezembro: o de 25/10 e o de 25/11

        s.Clock.UtcNow = SaoPaulo(25, 10);
        await Run(factory, s);
        (await Scalar<DateOnly>("SELECT settlement_date FROM transactions WHERE id = @t", ("t", await OccurrenceId(series.Id, "2026-10-25"))))
            .ShouldBe(new DateOnly(2026, 12, 5));
    }

    // --- Excluir a conta encerra as séries dela (decisão do dono, 2026-09-29; docs/fase-2.md, 2.14) ---
    // Só conta vazia pode ser excluída; as séries dela não teriam onde lançar e ficariam "ativas" sem conta.

    private async Task<Guid> NewAccount(Scenario s, object body) =>
        (await (await s.Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

    [Fact]
    public async Task Deleting_an_empty_account_ends_its_series_and_only_them()
    {
        var (factory, s) = await Start(SaoPaulo(29, 9));
        await using var _ = factory;
        using var __ = s;
        var nubank = await NewAccount(s, new { name = "Nubank", type = "Checking", initialBalanceCents = 0 });
        var rentFirst = await s.Buy(nubank, 100000, "2026-09-10", "Pix", "Aluguel");
        var rent = await s.RepeatOk(rentFirst);
        var other = await s.RepeatOk(await s.Buy(s.Checking, 50000, "2026-09-15", "Pix", "Diarista"));
        (await s.Client.DeleteAsync($"/transactions/{rentFirst}")).StatusCode.ShouldBe(HttpStatusCode.NoContent); // "só este"

        (await s.Client.DeleteAsync($"/accounts/{nubank}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var series = await s.List();
        var ended = series.Single(r => r.Id == rent.Id);
        (ended.IsEnded, ended.EndDate).ShouldBe((true, (DateOnly?)new DateOnly(2026, 9, 10)));
        series.Single(r => r.Id == other.Id).IsEnded.ShouldBeFalse();
        s.Clock.UtcNow = SaoPaulo(10, 10);
        await Run(factory, s);
        (await Generated(rent.Id)).ShouldBe(1); // só o primeiro, excluído
    }

    [Fact]
    public async Task A_refused_account_deletion_leaves_the_series_as_they_were()
    {
        var (factory, s) = await Start(SaoPaulo(29, 9));
        await using var _ = factory;
        using var __ = s;
        var nubank = await NewAccount(s, new { name = "Nubank", type = "Checking", initialBalanceCents = 0 });
        var rent = await s.RepeatOk(await s.Buy(nubank, 100000, "2026-09-10", "Pix", "Aluguel"));

        await ShouldFail(await s.Client.DeleteAsync($"/accounts/{nubank}"), HttpStatusCode.Conflict,
            "Esta conta tem transações. Mova ou exclua as transações antes, ou marque a conta como inativa.");

        var series = (await s.List()).Single(r => r.Id == rent.Id);
        (series.IsEnded, series.EndDate).ShouldBe((false, (DateOnly?)null));
    }

    // Série que já terminou pela data de término fica como estava: encerrar mudaria o término dela.
    [Fact]
    public async Task A_series_already_over_keeps_its_end_date()
    {
        var (factory, s) = await Start(SaoPaulo(29, 9));
        await using var _ = factory;
        using var __ = s;
        var nubank = await NewAccount(s, new { name = "Nubank", type = "Checking", initialBalanceCents = 0 });
        var first = await s.Buy(nubank, 100000, "2026-09-10", "Pix", "Aluguel");
        (await s.Repeat(first, endDate: "2026-10-05")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await s.Client.DeleteAsync($"/transactions/{first}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.DeleteAsync($"/accounts/{nubank}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.List()).Single(r => r.Description == "Aluguel").EndDate.ShouldBe(new DateOnly(2026, 10, 5));
    }

    // A pendência é do cartão em que a cobrança cairia: sem ele, não há onde lançá-la, e ela sai junto.
    [Fact]
    public async Task Deleting_the_card_of_a_pending_charge_discards_it()
    {
        var (factory, s) = await Start(SaoPaulo(29, 9));
        await using var _ = factory;
        using var __ = s;
        var elo = await NewAccount(s, new { name = "Elo", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });
        var food = await s.Buy(elo, 5000, "2026-09-20", description: "Ração"); // abre a fatura 2026-10
        var first = await s.Buy(elo, 40000, "2026-08-25");
        var october = await s.Statement(elo, "2026-10");
        (await s.Client.PostAsJsonAsync($"/statements/{october.Id}/pay", new { fromAccountId = s.Checking, date = "2026-09-27", method = "Boleto" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = await s.RepeatOk(first);
        series.Pendings.ShouldHaveSingleItem();
        // Esvazia o cartão: desfaz o pagamento e exclui as compras.
        foreach (var id in new[] { await PaymentOf(october.Id), food, first })
            (await s.Client.DeleteAsync($"/transactions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.DeleteAsync($"/accounts/{elo}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await s.List()).Single(r => r.Id == series.Id);
        (after.IsEnded, after.Pendings.Count).ShouldBe((true, 0));
    }
}
