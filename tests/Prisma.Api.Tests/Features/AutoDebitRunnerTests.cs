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

// docs/fase-2.md, 2.15 (etapa 2.26, tarefa 3): o gerador do débito automático contra o Postgres. As séries
// nascem pelo domínio (a API delas é a tarefa 4). Conta corrente Itaú. Horários em UTC: meia-noite e meia em
// São Paulo é 03:30 UTC.
[Collection(ApiCollection.Name)]
public sealed class AutoDebitRunnerTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id);

    private static DateTime SaoPaulo(int day, int month, int hour, int minute, int year = 2026) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(3);

    private static DateOnly D(int day, int month, int year = 2026) => new(year, month, day);

    private sealed record Generated(DateOnly PurchaseDate, DateOnly SettlementDate, long AmountCents, bool AmountEstimated, string Method);

    private sealed class Scenario(PrismaApiFactory factory, HttpClient client, FakeClock clock) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public FakeClock Clock { get; } = clock;
        public Guid UserId { get; private set; }
        public Guid Checking { get; private set; }

        public async Task Setup()
        {
            UserId = (await Client.GetFromJsonAsync<IdDto>("/auth/me"))!.Id;
            var response = await Client.PostAsJsonAsync("/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
            Checking = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        }

        // O primeiro débito, lançado pela pessoa, e a série a partir dele, pelo domínio.
        public async Task<Guid> AutoDebit(long amount, string date, string description, bool amountVaries = true, string? dueDate = null)
        {
            var response = await Client.PostAsJsonAsync("/transactions", new
            {
                accountId = Checking, type = "Expense", amountCents = amount, purchaseDate = date, method = "Debit", description,
            });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            var firstId = (await response.Content.ReadFromJsonAsync<List<TransactionDto>>())!.Single().Id;

            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopedUser>().UserId = UserId;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Transactions.SingleAsync(t => t.Id == firstId);
            var account = await db.Accounts.SingleAsync(a => a.Id == first.AccountId);
            var terms = new AutoDebitTerms(amountVaries, dueDate is null ? null : DateOnly.Parse(dueDate));
            var recurrence = Recurrence.StartFrom(first, account, RecurrenceFrequency.Monthly, null, terms).Value;
            db.Recurrences.Add(recurrence);
            await db.SaveChangesAsync();
            return recurrence.Id;
        }

        public Task<RecurrenceRunSummary> Run() =>
            factory.Services.GetRequiredService<RecurrenceRunner>().RunForUserAsync(UserId, CancellationToken.None);

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

    private async Task<NpgsqlDataReader> Query(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await connection.OpenAsync();
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteReaderAsync();
    }

    // O que a série gerou, pela data da ocorrência (o vencimento), sem o primeiro lançamento.
    private async Task<Dictionary<DateOnly, Generated>> GeneratedBy(Guid recurrence, DateOnly first)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await using var reader = await Query(connection,
            """
            SELECT occurrence_date, purchase_date, settlement_date, amount_cents, amount_estimated, method
            FROM transactions WHERE recurrence_id = @r AND occurrence_date <> @first
            """,
            ("r", recurrence), ("first", first));

        var generated = new Dictionary<DateOnly, Generated>();
        while (await reader.ReadAsync())
        {
            generated.Add(reader.GetFieldValue<DateOnly>(0), new Generated(
                reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt64(3), reader.GetBoolean(4),
                reader.GetString(5)));
        }

        return generated;
    }

    private async Task<DateOnly> GeneratedThrough(Guid recurrence)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await using var reader = await Query(connection, "SELECT generated_through FROM recurrences WHERE id = @r", ("r", recurrence));
        await reader.ReadAsync();
        return reader.GetFieldValue<DateOnly>(0);
    }

    // Sabesp vence em 15/11/2026, domingo: nada no domingo; na segunda à meia-noite e meia, com a estimativa.
    [Fact]
    public async Task The_sunday_due_date_is_debited_on_monday_with_the_estimate()
    {
        var (factory, s) = await Start(SaoPaulo(15, 11, 23, 30));
        await using var _ = factory;
        using var __ = s;
        var sabesp = await s.AutoDebit(9500, "2026-10-15", "Sabesp");

        (await s.Run()).Created.ShouldBe(0);
        (await GeneratedThrough(sabesp)).ShouldBe(D(15, 10));

        s.Clock.UtcNow = SaoPaulo(16, 11, 0, 30);
        (await s.Run()).Created.ShouldBe(1);
        (await s.Run()).Created.ShouldBe(0);

        (await GeneratedBy(sabesp, D(15, 10))).ShouldBe(new Dictionary<DateOnly, Generated>
        {
            [D(15, 11)] = new(D(16, 11), D(16, 11), 9500, true, "Debit"),
        });
        (await GeneratedThrough(sabesp)).ShouldBe(D(15, 11));
    }

    // Copel vence em 20/11/2026, sexta de Consciência Negra: o débito é na segunda, 23/11.
    [Fact]
    public async Task The_holiday_due_date_is_debited_on_the_next_business_day()
    {
        var (factory, s) = await Start(SaoPaulo(22, 11, 12, 0));
        await using var _ = factory;
        using var __ = s;
        var copel = await s.AutoDebit(18000, "2026-10-20", "Copel");

        (await s.Run()).Created.ShouldBe(0);

        s.Clock.UtcNow = SaoPaulo(23, 11, 0, 30);
        (await s.Run()).Created.ShouldBe(1);

        (await GeneratedBy(copel, D(20, 10)))[D(20, 11)].PurchaseDate.ShouldBe(D(23, 11));
    }

    // API fora do ar: em 18/11 a série alcança o atraso, e cada lançamento sai com a data do seu débito.
    [Fact]
    public async Task Catching_up_keeps_each_debit_date()
    {
        var (factory, s) = await Start(SaoPaulo(18, 11, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var sabesp = await s.AutoDebit(9500, "2026-09-15", "Sabesp");

        (await s.Run()).Created.ShouldBe(2);

        (await GeneratedBy(sabesp, D(15, 9))).ShouldBe(new Dictionary<DateOnly, Generated>
        {
            [D(15, 10)] = new(D(15, 10), D(15, 10), 9500, true, "Debit"),
            [D(15, 11)] = new(D(16, 11), D(16, 11), 9500, true, "Debit"),
        });
    }

    // Internet de valor fixo, vencimento 31/10 (sábado), com o domingo e Finados no caminho: sai em 03/11, sem
    // marca.
    [Fact]
    public async Task A_fixed_amount_is_debited_without_the_mark()
    {
        var (factory, s) = await Start(SaoPaulo(2, 11, 12, 0));
        await using var _ = factory;
        using var __ = s;
        var internet = await s.AutoDebit(12000, "2026-08-31", "Internet", amountVaries: false);

        (await s.Run()).Created.ShouldBe(1); // só a de 30/09 (setembro não tem 31)

        s.Clock.UtcNow = SaoPaulo(3, 11, 0, 30);
        (await s.Run()).Created.ShouldBe(1);

        (await GeneratedBy(internet, D(31, 8)))[D(31, 10)].ShouldBe(new Generated(D(3, 11), D(3, 11), 12000, false, "Debit"));
    }

    // A primeira ocorrência com vencimento antes da data do lançamento (regra 4): a Copel de 20/09, domingo,
    // debitada em 21/09. A série segue no dia 20, e o primeiro lançamento guarda o vencimento.
    [Fact]
    public async Task The_series_follows_the_first_due_date()
    {
        var (factory, s) = await Start(SaoPaulo(20, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var copel = await s.AutoDebit(18000, "2026-09-21", "Copel", dueDate: "2026-09-20");

        (await s.Run()).Created.ShouldBe(1);

        // Fora o de ocorrência 20/09 (o primeiro), só o de 20/10: o primeiro guardou o vencimento, não o 21/09.
        var generated = await GeneratedBy(copel, D(20, 9));
        generated.Keys.ShouldBe([D(20, 10)]);
        generated[D(20, 10)].PurchaseDate.ShouldBe(D(20, 10));
    }

    // Duas execuções ao mesmo tempo: só uma grava. Várias rodadas, porque a corrida nem sempre acontece.
    [Fact]
    public async Task Two_runs_at_once_generate_one_debit()
    {
        var (factory, s) = await Start(SaoPaulo(18, 11, 9, 0));
        await using var _ = factory;
        using var __ = s;

        for (var round = 0; round < 5; round++)
        {
            var sabesp = await s.AutoDebit(9500, "2026-09-15", "Sabesp");

            var runs = await Task.WhenAll(s.Run(), s.Run());

            runs.Sum(r => r.Failed).ShouldBe(0);
            (await GeneratedBy(sabesp, D(15, 9))).Keys.Order().ShouldBe([D(15, 10), D(15, 11)]);
        }
    }

    // O banco guarda as regras também (2.15, regra 2 e 5): nem um ajuste à mão cria débito automático semanal
    // ou marca de conferir fora de uma série.
    [Theory]
    [InlineData("UPDATE recurrences SET frequency = 'Weekly' WHERE id = @r", "ck_recurrences_auto_debit")]
    [InlineData("UPDATE recurrences SET method = 'Pix' WHERE id = @r", "ck_recurrences_auto_debit")]
    [InlineData("UPDATE recurrences SET kind = 'Regular' WHERE id = @r", "ck_recurrences_amount_varies")]
    [InlineData("UPDATE transactions SET amount_estimated = true WHERE recurrence_id IS NULL AND user_id = @u", "ck_transactions_amount_estimated")]
    public async Task The_database_refuses_what_the_rules_forbid(string sql, string constraint)
    {
        var (factory, s) = await Start(SaoPaulo(20, 10, 9, 0));
        await using var _ = factory;
        using var __ = s;
        var sabesp = await s.AutoDebit(9500, "2026-10-15", "Sabesp");
        await s.Client.PostAsJsonAsync("/transactions", new
        {
            accountId = s.Checking, type = "Expense", amountCents = 1000, purchaseDate = "2026-10-16", method = "Pix", description = "Padaria",
        });

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("r", sabesp);
        command.Parameters.AddWithValue("u", s.UserId);

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        error.ConstraintName.ShouldBe(constraint);
    }
}
