using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/admin-email.md, 2: ao criar a coluna do último acesso, quem já confirmou o e-mail (e pôde usar o app) fica com o
// instante da migration, para não parecer sumido; quem nunca confirmou fica vazio. Num banco próprio, migrado até antes.
[Collection(ApiCollection.Name)]
public sealed class LastSeenMigrationTests(PostgresFixture postgres)
{
    private const string Before = "20261028020049_AddAccountTracksCash";

    [Fact]
    public async Task Confirmed_accounts_are_stamped_and_unconfirmed_ones_stay_empty()
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

            var confirmed = Guid.CreateVersion7();
            var unconfirmed = Guid.CreateVersion7();
            await Execute(connectionString, $"""
                INSERT INTO users (id, access_failed_count, category_catalog_version, created_at, email_confirmed, lockout_enabled, phone_number_confirmed, two_factor_enabled)
                VALUES ('{confirmed}', 0, 1, '2026-01-01', true, true, false, false),
                       ('{unconfirmed}', 0, 1, '2026-01-01', false, true, false, false);
                """);

            var before = DateTime.UtcNow;
            await migrator.MigrateAsync();

            (await LastSeen(connectionString, confirmed)).ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(before.AddMinutes(-1));
            (await LastSeen(connectionString, unconfirmed)).ShouldBeNull();
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

    private static async Task<DateTime?> LastSeen(string connectionString, Guid userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT last_seen_at FROM users WHERE id = '{userId}'", connection);
        var value = await command.ExecuteScalarAsync();
        return value is DateTime date ? date : null;
    }
}
