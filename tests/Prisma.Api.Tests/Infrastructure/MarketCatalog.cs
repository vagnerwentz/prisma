using Npgsql;

namespace Prisma.Api.Tests.Infrastructure;

// O catálogo de ativos é global: os testes dele começam com a tabela vazia. Proventos e carteiras apontam para os
// ativos (chave estrangeira), então saem antes.
public static class MarketCatalog
{
    public static async Task ClearAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM transactions WHERE asset_id IS NOT NULL; DELETE FROM holdings; DELETE FROM asset_logos; DELETE FROM assets;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
