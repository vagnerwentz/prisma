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

// Etapa A.3: o que a pessoa exclui fica restaurável pelo "Desfazer" por 30 minutos e depois sai do banco de vez (a
// política promete "em até 1 hora"). Quem ainda é apontado por algo vivo perde só o vínculo.
[Collection(ApiCollection.Name)]
public sealed class DeletedItemsPurgeTests(PostgresFixture postgres)
{
    private sealed record IdDto(Guid Id);

    private sealed record TransactionDto(Guid Id, Guid? InstallmentPurchaseId, Guid? RefundedTransactionId);

    private sealed record AssetDto(Guid Id, string Symbol);

    private static readonly DateTime Start = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Start);
    private readonly FakeAssetListSource _source = new();

    private PrismaApiFactory Factory(Action<IServiceCollection>? services = null) =>
        new(postgres.ConnectionString, null, _clock, null, services);

    private static async Task<T> Post<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> PostId(HttpClient client, string url, object body) => (await Post<IdDto>(client, url, body)).Id;

    private static async Task<Guid> Expense(HttpClient client, Guid account, Guid? category = null, string method = "Pix") =>
        (await Post<List<IdDto>>(client, "/transactions", new
        {
            accountId = account, type = "Expense", amountCents = 4590, purchaseDate = "2026-11-01", categoryId = category,
            method, description = "Petz",
        }))[0].Id;

    private static Task<Guid> Checking(HttpClient client) =>
        PostId(client, "/accounts", new { name = "Itaú", type = "Checking", initialBalanceCents = 0 });

    private static Task<Guid> Card(HttpClient client) =>
        PostId(client, "/accounts", new { name = "Visa", type = "CreditCard", initialBalanceCents = 0, closingDay = 5, dueDay = 12 });

    private async Task Purge(PrismaApiFactory factory, TimeSpan after)
    {
        _clock.UtcNow = Start + after;
        await factory.Services.GetRequiredService<ErasureRunner>().RunAsync(CancellationToken.None);
    }

    // Lê o banco direto, sem o filtro do soft delete: o que conta é a linha existir.
    private async Task<long> Count(string table, Guid id, string column = "id")
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {column} = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<object?> Column(string table, string column, Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT {column} FROM {table} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    [Fact]
    public async Task Deleted_transaction_can_be_restored_for_30_minutes_then_is_gone()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await Checking(client);
        var undone = await Expense(client, account);
        var gone = await Expense(client, account);
        var alive = await Expense(client, account);
        (await client.DeleteAsync($"/transactions/{undone}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/transactions/{gone}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(29));
        (await Count("transactions", gone)).ShouldBe(1);
        (await client.PostAsync($"/transactions/{undone}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Purge(factory, TimeSpan.FromMinutes(31));
        (await Count("transactions", gone)).ShouldBe(0);
        (await client.PostAsync($"/transactions/{gone}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Count("transactions", undone)).ShouldBe(1);
        (await Count("transactions", alive)).ShouldBe(1);
    }

    [Fact]
    public async Task Deleted_installment_purchase_goes_with_its_installments()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var card = await Card(client);
        var installments = await Post<List<TransactionDto>>(client, "/transactions", new
        {
            accountId = card, type = "Expense", amountCents = 30000, purchaseDate = "2026-11-01", method = "Credit",
            description = "Notebook", installments = 3,
        });
        var purchase = installments[0].InstallmentPurchaseId!.Value;
        (await client.DeleteAsync($"/installment-purchases/{purchase}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(31));

        (await Count("installment_purchases", purchase)).ShouldBe(0);
        (await Count("transactions", purchase, "installment_purchase_id")).ShouldBe(0);
    }

    // A compra sai; o estorno que a pessoa manteve fica, sem o vínculo (vira estorno avulso).
    [Fact]
    public async Task Refund_of_a_purged_purchase_stays_as_a_standalone_refund()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var card = await Card(client);
        var purchase = await Expense(client, card, method: "Credit");
        var refund = (await Post<List<IdDto>>(client, "/transactions", new
        {
            accountId = card, type = "Refund", amountCents = 2000, purchaseDate = "2026-11-02", method = "Credit",
            description = "Estorno", refundedTransactionId = purchase,
        }))[0].Id;
        (await client.DeleteAsync($"/transactions/{purchase}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(31));

        (await Count("transactions", purchase)).ShouldBe(0);
        (await Count("transactions", refund)).ShouldBe(1);
        (await Column("transactions", "refunded_transaction_id", refund)).ShouldBeNull();
        (await client.GetAsync("/transactions")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // A série cuja categoria foi apagada continua, sem categoria; subcategoria e categoria saem.
    [Fact]
    public async Task Purged_category_leaves_the_series_without_a_category()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await Checking(client);
        var category = await PostId(client, "/categories", new { name = "Pets", type = "Expense" });
        var subcategory = await PostId(client, "/categories", new { name = "Ração", type = "Expense", parentCategoryId = category });
        var first = await Expense(client, account, subcategory);
        var series = await Post<IdDto>(client, $"/transactions/{first}/recurrence", new { frequency = "Monthly" });
        (await client.DeleteAsync($"/transactions/{first}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/categories/{subcategory}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/categories/{category}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(31));

        (await Count("categories", subcategory)).ShouldBe(0);
        (await Count("categories", category)).ShouldBe(0);
        (await Count("recurrences", series.Id)).ShouldBe(1);
        (await Column("recurrences", "category_id", series.Id)).ShouldBeNull();
    }

    // A conta apagada (só vazia) leva as faturas e as séries dela, que já estavam encerradas.
    [Fact]
    public async Task Purged_account_takes_its_statements_and_series()
    {
        await using var factory = Factory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var card = await Card(client);
        var purchase = await Expense(client, card, method: "Credit");
        var series = await Post<IdDto>(client, $"/transactions/{purchase}/recurrence", new { frequency = "Monthly" });
        (await Count("statements", card, "account_id")).ShouldBePositive();
        (await client.DeleteAsync($"/transactions/{purchase}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/accounts/{card}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(31));

        (await Count("accounts", card)).ShouldBe(0);
        (await Count("statements", card, "account_id")).ShouldBe(0);
        (await Count("recurrences", series.Id)).ShouldBe(0);
        (await Count("transactions", purchase)).ShouldBe(0);
    }

    [Fact]
    public async Task Portfolio_items_are_purged_and_the_note_goes_with_its_lines()
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        await using var factory = Factory(services => services.AddScoped<IAssetListSource>(_ => _source));
        _source.Assets = [Listed("ITUB3", AssetKind.Stock, "ITAU UNIBANCO HOLDING S.A.")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        var client = await factory.CreateAuthenticatedClientAsync();
        var itub3 = (await client.GetFromJsonAsync<List<AssetDto>>("/assets?q=ITUB3"))!.First(a => a.Symbol == "ITUB3").Id;

        var note = await PostId(client, "/trade-notes", new
        {
            broker = "XP", date = "2026-10-01", feesCents = 0, brokerageFeeCents = 0, brokerageFeeOnAllKinds = false,
            trades = new[] { new { assetId = itub3, side = "Buy", quantity = 10, unitPriceCents = 3000 } },
        });
        var request = (await (await client.PostAsJsonAsync("/asset-requests", new { symbol = "CVBI11" })).Content.ReadFromJsonAsync<IdDto>())!.Id;
        (await client.DeleteAsync($"/trade-notes/{note}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/asset-requests/{request}")).EnsureSuccessStatusCode();
        var holding = (await (await client.PostAsJsonAsync("/holdings", new { assetId = itub3 })).Content.ReadFromJsonAsync<IdDto>())!.Id;
        (await client.DeleteAsync($"/holdings/{holding}")).EnsureSuccessStatusCode();

        await Purge(factory, TimeSpan.FromMinutes(31));

        (await Count("trade_notes", note)).ShouldBe(0);
        (await Count("trades", note, "trade_note_id")).ShouldBe(0);
        (await Count("asset_requests", request)).ShouldBe(0);
        (await Count("holdings", holding)).ShouldBe(0);
        // Pôr o ativo de novo na carteira cria outro registro, sem erro.
        (await client.PostAsJsonAsync("/holdings", new { assetId = itub3 })).IsSuccessStatusCode.ShouldBeTrue();
    }

    // Toda tabela com exclusão pelo "Desfazer" é limpa (ou sai com a conta). Tabela nova com deleted_at que não entre na
    // limpeza quebra este teste, antes de a política deixar de ser verdade.
    [Fact]
    public async Task Every_table_with_soft_delete_is_purged()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'deleted_at' AND table_name <> 'deleted_accounts'",
            connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        tables.ShouldBe(ErasureRunner.PurgedTables.Concat(ErasureRunner.PurgedWithAccount), ignoreOrder: true);
        foreach (var table in ErasureRunner.PurgedTables)
            ErasureRunner.PurgeStatements.ShouldContain(s => s.Contains($"DELETE FROM {table} ", StringComparison.Ordinal), table);
    }
}
