using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Recurrences;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain.Recurrences;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.14 (etapa 2.25, tarefa 3): o gerador dos lançamentos que se repetem contra o
// Postgres. As séries nascem pelo domínio (a API delas é a tarefa 5). Visa "fecha 26, vence 5".
// Horários em UTC: meia-noite e meia em São Paulo é 03:30 UTC.
[Collection(ApiCollection.Name)]
public sealed class RecurrenceRunnerTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id);

    private sealed record StatementDto(Guid Id, string Reference, bool IsPaid, long TotalCents);

    private static DateTime SaoPaulo(int day, int month, int hour, int minute, int year = 2026) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(3);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client, FakeClock clock) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public FakeClock Clock { get; } = clock;
        public Guid UserId { get; private set; }
        public Guid Checking { get; private set; }
        public Guid Visa { get; private set; }

        public RecurrenceRunner Runner => factory.Services.GetRequiredService<RecurrenceRunner>();

        public async Task Setup()
        {
            UserId = (await Client.GetFromJsonAsync<IdDto>("/auth/me"))!.Id;
            Checking = await Account(new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            await NewCard();
        }

        public async Task NewCard() =>
            Visa = await Account(new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 26, dueDay = 5 });

        private async Task<Guid> Account(object body) =>
            (await (await Client.PostAsJsonAsync("/accounts", body)).Content.ReadFromJsonAsync<IdDto>())!.Id;

        public async Task<Guid> Buy(Guid account, long amount, string date, string method = "Credit")
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = account, type = "Expense", amountCents = amount, purchaseDate = date, method, description = "Pet",
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.Single().Id;
        }

        // Cria a série pelo domínio, no escopo do usuário, como a API da tarefa 5 fará.
        public async Task<Guid> Repeat(Guid transactionId, RecurrenceFrequency frequency = RecurrenceFrequency.Monthly)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopedUser>().UserId = UserId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Transactions.SingleAsync(t => t.Id == transactionId);
            var account = await db.Accounts.SingleAsync(a => a.Id == first.AccountId);
            var recurrence = Recurrence.StartFrom(first, account, frequency, null).Value;
            db.Recurrences.Add(recurrence);
            await db.SaveChangesAsync();
            return recurrence.Id;
        }

        public Task<RecurrenceRunSummary> Run() => Runner.RunForUserAsync(UserId, CancellationToken.None);

        public Task<HttpResponseMessage> Pay(Guid statementId, string date) =>
            Client.PostAsJsonAsync($"/statements/{statementId}/pay", new { fromAccountId = Checking, date, method = "Boleto" });

        public async Task<StatementDto> Statement(Guid card, string reference) =>
            (await Client.GetFromJsonAsync<List<StatementDto>>($"/accounts/{card}/statements"))!.Single(s => s.Reference == reference);

        public void Dispose() => Client.Dispose();
    }

    private async Task<(PrismaApiFactory, Scenario)> Start(DateTime utcNow)
    {
        var clock = new FakeClock(utcNow);
        var factory = new PrismaApiFactory(postgres.ConnectionString, clock: clock);
        var s = new Scenario(factory, await factory.CreateAuthenticatedClientAsync(), clock);
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

    // Lançamentos da série, contando os excluídos.
    private Task<long> Generated(Guid recurrence, string? occurrence = null) =>
        occurrence is null
            ? Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r", ("r", recurrence))
            : Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r AND occurrence_date = @d",
                ("r", recurrence), ("d", DateOnly.Parse(occurrence)));

    [Fact]
    public async Task The_pet_of_october_25_is_generated_once_on_the_card()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10, 0, 30));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));

        (await s.Run()).Created.ShouldBe(1);
        (await s.Run()).Created.ShouldBe(0);

        (await Generated(recurrence, "2026-10-25")).ShouldBe(1);
        (await Generated(recurrence)).ShouldBe(2); // a de 25/09, que criou a série, e a de 25/10
        (await Scalar<DateOnly>("SELECT settlement_date FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'",
            ("r", recurrence))).ShouldBe(new DateOnly(2026, 11, 5));
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(40000);
        (await Scalar<DateOnly>("SELECT generated_through FROM recurrences WHERE id = @r", ("r", recurrence)))
            .ShouldBe(new DateOnly(2026, 10, 25));
    }

    // 23:30 do dia 24 em São Paulo já é dia 25 em UTC: ainda não é o dia.
    [Fact]
    public async Task Nothing_is_generated_before_midnight_in_sao_paulo()
    {
        var (factory, s) = await Start(SaoPaulo(24, 10, 23, 30));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));

        (await s.Run()).Created.ShouldBe(0);

        (await Generated(recurrence)).ShouldBe(1);
    }

    // Duas execuções ao mesmo tempo: só uma grava; a outra encontra a trava. Várias rodadas, cada uma numa
    // série nova, porque a corrida nem sempre acontece.
    [Fact]
    public async Task Two_runs_at_once_generate_one_transaction()
    {
        var (factory, s) = await Start(SaoPaulo(11, 12, 9, 0));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            var recurrence = await s.Repeat(await s.Buy(s.Checking, 100000, "2026-09-10", "Pix"));

            var runs = await Task.WhenAll(s.Run(), s.Run());

            runs.Sum(r => r.Failed).ShouldBe(0);
            foreach (var date in new[] { "2026-10-10", "2026-11-10", "2026-12-10" })
                (await Generated(recurrence, date)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Rent_by_pix_catches_up_every_missed_month()
    {
        var (factory, s) = await Start(SaoPaulo(11, 12, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Checking, 100000, "2026-09-10", "Pix"));

        (await s.Run()).Created.ShouldBe(3);

        (await Generated(recurrence)).ShouldBe(4);
    }

    // O índice único conta os excluídos: mesmo que a série volte atrás (uma falha, um ajuste à mão no
    // banco), o lançamento que a pessoa excluiu nunca é recriado.
    [Fact]
    public async Task A_deleted_occurrence_never_comes_back()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));
        await s.Run();
        var generated = await Scalar<Guid>("SELECT id FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'", ("r", recurrence));
        (await s.Client.DeleteAsync($"/transactions/{generated}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Scalar<int>("UPDATE recurrences SET generated_through = '2026-09-25' WHERE id = @r RETURNING 0", ("r", recurrence));

        var again = await s.Run();

        (again.Created, again.Failed).ShouldBe((0, 0));
        (await Generated(recurrence, "2026-10-25")).ShouldBe(1);
        (await Scalar<long>("SELECT count(*) FROM transactions WHERE recurrence_id = @r AND deleted_at IS NULL", ("r", recurrence)))
            .ShouldBe(1); // só a de 25/09
    }

    [Fact]
    public async Task An_inactive_card_skips_its_occurrences()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));
        (await s.Client.PatchAsJsonAsync($"/accounts/{s.Visa}", new
        {
            name = "Visa", initialBalanceCents = 0, closingDay = 26, dueDay = 5, creditLimitCents = (long?)null, isActive = false,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await s.Run()).Created.ShouldBe(0);

        (await Generated(recurrence)).ShouldBe(1);
        (await Scalar<DateOnly>("SELECT generated_through FROM recurrences WHERE id = @r", ("r", recurrence)))
            .ShouldBe(new DateOnly(2026, 10, 25));
    }

    // A geração atrasou: a fatura de novembro fechou em 26/10 e foi paga em 27/10; a tarefa só rodou em 28/10.
    [Fact]
    public async Task A_charge_on_a_paid_statement_becomes_pending()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));
        await s.Buy(s.Visa, 5000, "2026-10-20");
        (await s.Pay((await s.Statement(s.Visa, "2026-11")).Id, "2026-10-27")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var run = await s.Run();

        (run.Created, run.Pending).ShouldBe((0, 1));
        (await Generated(recurrence)).ShouldBe(1);
        (await s.Statement(s.Visa, "2026-11")).TotalCents.ShouldBe(5000);
        (await Scalar<string>("SELECT statement_reference FROM recurrence_pendings WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'",
            ("r", recurrence))).ShouldBe("2026-11");
    }

    // Pagar a fatura enquanto a tarefa põe a cobrança nela: a fatura paga nunca fica com valor diferente do
    // pagamento. Se a tarefa perde, a próxima execução encontra a fatura paga e deixa a cobrança pendente.
    [Fact]
    public async Task Generating_while_the_statement_is_paid_never_changes_a_paid_total()
    {
        var (factory, s) = await Start(SaoPaulo(28, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            await s.NewCard();
            await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));
            await s.Buy(s.Visa, 5000, "2026-10-20");
            var november = await s.Statement(s.Visa, "2026-11");

            var pay = s.Pay(november.Id, "2026-10-27");
            await Task.WhenAll(s.Run(), pay);
            await s.Run();

            november = await s.Statement(s.Visa, "2026-11");
            if (november.IsPaid)
                (await Scalar<long>("SELECT amount_cents FROM transactions WHERE statement_id = @s AND type = 'Transfer'", ("s", november.Id)))
                    .ShouldBe(november.TotalCents);
            else
                november.TotalCents.ShouldBe(45000);
        }
    }

    // A tarefa em segundo plano (tarefa 4) gera ao subir a API, sem ninguém chamar o gerador.
    [Fact]
    public async Task The_api_generates_when_it_starts()
    {
        var (factory, s) = await Start(SaoPaulo(25, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var recurrence = await s.Repeat(await s.Buy(s.Visa, 40000, "2026-09-25"));

        var logs = new LogSink();
        await using var withWorker = new PrismaApiFactory(postgres.ConnectionString,
            new Dictionary<string, string> { ["Recurrences:Runner:Enabled"] = "true" }, s.Clock, logs);
        withWorker.Services.ShouldNotBeNull(); // sobe a API

        await logs.WaitFor(e => e.EventId.Name == "RecurrenceRunFinished");
        (await Generated(recurrence, "2026-10-25")).ShouldBe(1);
    }

    // A execução de todos os usuários: cada um no seu escopo, cada lançamento com o seu dono.
    [Fact]
    public async Task Each_user_generates_only_their_own_series()
    {
        var (factory, a) = await Start(SaoPaulo(25, 10, 9, 0));
        await using var _ = factory;
        using var __ = a;
        using var b = new Scenario(factory, await factory.CreateAuthenticatedClientAsync(), a.Clock);
        await b.Setup();
        var ra = await a.Repeat(await a.Buy(a.Visa, 40000, "2026-09-25"));
        var rb = await b.Repeat(await b.Buy(b.Checking, 100000, "2026-09-25", "Pix"));

        (await a.Runner.RunAsync(CancellationToken.None)).Failed.ShouldBe(0);

        (await Scalar<Guid>("SELECT user_id FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'", ("r", ra)))
            .ShouldBe(a.UserId);
        (await Scalar<Guid>("SELECT user_id FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'", ("r", rb)))
            .ShouldBe(b.UserId);
        (await Scalar<Guid>("SELECT account_id FROM transactions WHERE recurrence_id = @r AND occurrence_date = '2026-10-25'", ("r", rb)))
            .ShouldBe(b.Checking);
    }
}
