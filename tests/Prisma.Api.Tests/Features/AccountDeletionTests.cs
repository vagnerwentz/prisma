using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Infrastructure.Privacy;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// Etapa A.3: "Apagar conta". Com a senha, a conta e tudo o que é dela saem do banco de vez; a dos outros fica. O id vai
// para deleted_accounts, e a conta que voltar com um backup restaurado é apagada de novo.
[Collection(ApiCollection.Name)]
public sealed class AccountDeletionTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id, Guid? StatementId, Guid? InstallmentPurchaseId);

    private sealed record AssetDto(Guid Id, string Symbol);

    private sealed record ProblemDto(string? Detail, string? Code);

    private readonly FakeClock _clock = new(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc));
    private readonly FakeAssetListSource _source = new();
    private readonly LogSink _logs = new();

    private async Task<PrismaApiFactory> Start()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, _logs,
            services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets = [Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A.")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        return factory;
    }

    // Um pouco de tudo, pela API como a tela faria: contas, categoria e subcategoria, lançamento vivo e excluído, compra
    // parcelada no cartão com estorno ligado, transferência, série, carteira, nota e aviso de ativo.
    private static async Task<(HttpClient Client, Guid UserId)> PersonWithData(PrismaApiFactory factory)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var me = await client.GetFromJsonAsync<IdDto>("/auth/me");

        var checking = await PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
        var cash = await PostId(client, "/accounts", new { name = "Carteira", type = "Cash", initialBalanceCents = 0 });
        var card = await PostId(client, "/accounts",
            new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });
        var category = await PostId(client, "/categories", new { name = "Pets", type = "Expense" });
        await PostId(client, "/categories", new { name = "Ração", type = "Expense", parentCategoryId = category });

        var simple = new { accountId = checking, type = "Expense", amountCents = 4590, purchaseDate = "2026-10-10", categoryId = category, method = "Pix", description = "Petz" };
        var kept = (await Post<List<IdDto>>(client, "/transactions", simple))[0].Id;
        var deleted = (await Post<List<IdDto>>(client, "/transactions", simple))[0].Id;
        (await client.DeleteAsync($"/transactions/{deleted}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/transactions/{kept}/recurrence", new { frequency = "Monthly" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var installments = await Post<List<TransactionDto>>(client, "/transactions",
            new { accountId = card, type = "Expense", amountCents = 30000, purchaseDate = "2026-10-10", method = "Credit", description = "Notebook", installments = 3 });
        await Post<List<IdDto>>(client, "/transactions", new
        {
            accountId = card, type = "Refund", amountCents = 2000, purchaseDate = "2026-10-15", method = "Credit",
            description = "Estorno", refundedTransactionId = installments[0].Id,
        });
        await Post<List<IdDto>>(client, "/transfers",
            new { fromAccountId = checking, toAccountId = cash, amountCents = 5000, date = "2026-10-15", method = "Pix" });

        var itub3 = (await client.GetFromJsonAsync<List<AssetDto>>("/assets?q=ITUB3"))!.First(a => a.Symbol == "ITUB3").Id;
        (await client.PostAsJsonAsync("/holdings", new { assetId = itub3 })).EnsureSuccessStatusCode();
        await PostId(client, "/trade-notes", new
        {
            broker = "XP", date = "2026-10-01", feesCents = 0, brokerageFeeCents = 0, brokerageFeeOnAllKinds = false,
            trades = new[] { new { assetId = itub3, side = "Buy", quantity = 10, unitPriceCents = 3000 } },
        });
        (await client.PostAsJsonAsync("/asset-requests", new { symbol = "CVBI11" })).EnsureSuccessStatusCode();
        return (client, me!.Id);
    }

    private static async Task<T> Post<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> PostId(HttpClient client, string url, object body) => (await Post<IdDto>(client, url, body)).Id;

    private static Task<HttpResponseMessage> Delete(HttpClient client, string password) =>
        client.PostAsJsonAsync("/auth/delete-account", new { password });

    // Linhas da pessoa em cada tabela que tem user_id, mais a dela em users: lido do schema, então tabela nova entra sozinha.
    private async Task<Dictionary<string, long>> RowsOf(Guid userId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'user_id' AND table_name <> 'deleted_accounts' ORDER BY 1",
            connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        tables.ShouldContain("transactions");

        var rows = new Dictionary<string, long>();
        foreach (var table in tables.Append("users"))
        {
            var column = table == "users" ? "id" : "user_id";
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {column} = @user", connection);
            count.Parameters.AddWithValue("user", userId);
            rows[table] = (long)(await count.ExecuteScalarAsync())!;
        }

        return rows;
    }

    private async Task<long> Scalar(string sql, Guid userId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user", userId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Deleting_the_account_erases_every_row_of_the_person_and_nothing_of_the_others()
    {
        await using var factory = await Start();
        var (ana, anaId) = await PersonWithData(factory);
        var (bruno, brunoId) = await PersonWithData(factory);
        var brunoBefore = await RowsOf(brunoId);
        var anaBefore = await RowsOf(anaId);
        foreach (var table in new[] { "accounts", "categories", "transactions", "statements", "installment_purchases",
                     "recurrences", "holdings", "trade_notes", "asset_requests", "users" })
            anaBefore[table].ShouldBePositive(table);

        var response = await Delete(ana, PrismaApiFactory.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RowsOf(anaId)).Where(r => r.Value > 0).ShouldBeEmpty();
        (await Scalar("SELECT count(*) FROM trades t JOIN trade_notes n ON n.id = t.trade_note_id WHERE n.user_id = @user", anaId)).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM deleted_accounts WHERE user_id = @user", anaId)).ShouldBe(1);
        (await RowsOf(brunoId)).ShouldBe(brunoBefore);
        (await bruno.GetAsync("/transactions")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _logs.Entries.ShouldContain(l => l.EventId.Name == "AccountDeleted" && Equals(l["UserId"], anaId));
    }

    [Fact]
    public async Task After_deleting_the_session_ends_and_the_email_no_longer_signs_in()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        var email = $"{Guid.NewGuid():N}@teste.com.br";
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true })).EnsureSuccessStatusCode();
        await factory.ConfirmEmailAsync(email);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password })).EnsureSuccessStatusCode();

        (await Delete(client, PrismaApiFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/auth/login", new { email, password = PrismaApiFactory.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // O mesmo e-mail pode criar uma conta nova, que nasce vazia.
        (await client.PostAsJsonAsync("/auth/register", new { email, password = PrismaApiFactory.Password, acceptedTerms = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Wrong_password_keeps_the_account_and_the_session()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        var client = await factory.CreateAuthenticatedClientAsync();
        var me = (await client.GetFromJsonAsync<IdDto>("/auth/me"))!;
        await PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });

        var response = await Delete(client, "outra-senha-123");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDto>())!.Code.ShouldBe("WrongPassword");
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RowsOf(me.Id))["accounts"].ShouldBe(1);
        (await Scalar("SELECT count(*) FROM deleted_accounts WHERE user_id = @user", me.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_session_nothing_is_deleted()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        var anonymous = factory.CreateHttpsClient();

        (await Delete(anonymous, PrismaApiFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Toda tabela com dono sai com a conta: pelo AccountEraser ou pela cascata do banco a partir de users. Tabela nova
    // com user_id que não esteja em nenhum dos dois quebra este teste antes de quebrar o "Apagar conta" em produção.
    [Fact]
    public async Task Every_table_with_an_owner_is_erased_by_the_eraser_or_by_cascade()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT c.table_name,
                   EXISTS (SELECT 1 FROM pg_constraint k
                           WHERE k.contype = 'f' AND k.confdeltype = 'c'
                             AND k.conrelid = c.table_name::regclass AND k.confrelid = 'users'::regclass)
            FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.column_name = 'user_id' AND c.table_name <> 'deleted_accounts'
            """, connection);
        var missing = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var (table, cascades) = (reader.GetString(0), reader.GetBoolean(1));
            if (!cascades && !AccountEraserCovers(table))
                missing.Add(table);
        }

        missing.ShouldBeEmpty();
    }

    private static bool AccountEraserCovers(string table) =>
        AccountEraser.Statements.Any(s => s.StartsWith($"DELETE FROM {table} WHERE user_id = @user", StringComparison.Ordinal));

    // Restaurado um backup de antes da exclusão, a conta volta; a tarefa a apaga de novo pela lista.
    [Fact]
    public async Task An_account_that_comes_back_with_a_backup_is_erased_again()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock, _logs);
        var client = await factory.CreateAuthenticatedClientAsync();
        var me = (await client.GetFromJsonAsync<IdDto>("/auth/me"))!;
        await PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO deleted_accounts (user_id, deleted_at) VALUES (@user, @at)", connection);
            insert.Parameters.AddWithValue("user", me.Id);
            insert.Parameters.AddWithValue("at", _clock.UtcNow.AddDays(-2));
            await insert.ExecuteNonQueryAsync();
        }

        var summary = await factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        summary.AccountsReapplied.ShouldBeGreaterThanOrEqualTo(1);
        (await RowsOf(me.Id)).Where(r => r.Value > 0).ShouldBeEmpty();
        (await client.GetAsync("/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _logs.Entries.ShouldContain(l => l.EventId.Name == "AccountDeletionReapplied" && Equals(l["UserId"], me.Id));
        // A data é a da exclusão original: os 90 dias contam dela, não da reaplicação.
        await using var check = new NpgsqlConnection(postgres.ConnectionString);
        await check.OpenAsync();
        await using var at = new NpgsqlCommand("SELECT deleted_at FROM deleted_accounts WHERE user_id = @user", check);
        at.Parameters.AddWithValue("user", me.Id);
        ((DateTime)(await at.ExecuteScalarAsync())!).ShouldBe(_clock.UtcNow.AddDays(-2));
    }

    // Depois de 90 dias não há backup daquele dia: o id é esquecido.
    [Fact]
    public async Task Deleted_accounts_are_forgotten_after_90_days()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString, null, _clock);
        var (old, recent) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO deleted_accounts (user_id, deleted_at) VALUES (@old, @oldAt), (@recent, @recentAt)", connection);
            insert.Parameters.AddWithValue("old", old);
            insert.Parameters.AddWithValue("oldAt", _clock.UtcNow.AddDays(-91));
            insert.Parameters.AddWithValue("recent", recent);
            insert.Parameters.AddWithValue("recentAt", _clock.UtcNow.AddDays(-89));
            await insert.ExecuteNonQueryAsync();
        }

        await factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);

        (await Scalar("SELECT count(*) FROM deleted_accounts WHERE user_id = @user", old)).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM deleted_accounts WHERE user_id = @user", recent)).ShouldBe(1);
    }
}
