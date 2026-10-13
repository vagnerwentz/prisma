using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, "I.7b3": as notas lançadas antes da corretagem por tipo ficam com o mesmo rateio. A marcação
// ligada vira todos os tipos; desligada, só ação. Num banco próprio, migrado até antes, com notas no formato antigo.
[Collection(ApiCollection.Name)]
public sealed class BrokerageFeeKindsMigrationTests(PostgresFixture postgres)
{
    private const string Before = "20261008170453_AddAssetRequests";

    [Fact]
    public async Task Old_mark_becomes_kinds()
    {
        var database = $"migration_{Guid.NewGuid():N}";
        await Execute(postgres.ConnectionString, $"CREATE DATABASE {database}");
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        try
        {
            await using var factory = new PrismaApiFactory(connectionString);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Before);

            var onAll = Guid.CreateVersion7();
            var stocksOnly = Guid.CreateVersion7();
            // Sem usuário de verdade: a chave estrangeira fica de fora só nesta sessão.
            await Execute(connectionString, $"""
                SET session_replication_role = replica;
                INSERT INTO trade_notes (id, user_id, broker, trade_date, fees_cents, brokerage_fee_cents, brokerage_fee_on_all_kinds, created_at, updated_at)
                VALUES ('{onAll}', '{Guid.CreateVersion7()}', 'XP', '2024-03-10', 0, 490, true, now(), now()),
                       ('{stocksOnly}', '{Guid.CreateVersion7()}', 'XP', '2024-03-10', 0, 490, false, now(), now());
                """);

            await migrator.MigrateAsync();

            (await Kinds(connectionString, onAll)).ShouldBe(["Stock", "Fii", "Etf", "FiInfra", "FiAgro", "Fip", "Fidc", "OtherFund", "Bdr", "Unknown"]);
            (await Kinds(connectionString, stocksOnly)).ShouldBe(["Stock"]);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Execute(postgres.ConnectionString, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> Kinds(string connectionString, Guid noteId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT brokerage_fee_kinds FROM trade_notes WHERE id = '{noteId}'", connection);
        return (string[])(await command.ExecuteScalarAsync())!;
    }
}
