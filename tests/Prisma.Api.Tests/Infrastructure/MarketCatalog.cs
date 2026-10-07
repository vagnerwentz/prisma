using Npgsql;

namespace Prisma.Api.Tests.Infrastructure;

// O catálogo de ativos é global: os testes dele começam com a tabela vazia. Proventos, notas, carteiras e eventos apontam para os
// ativos (chave estrangeira), então saem antes.
public static class MarketCatalog
{
    public static async Task ClearAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM transactions WHERE asset_id IS NOT NULL; DELETE FROM trades; DELETE FROM trade_notes; DELETE FROM holdings; DELETE FROM corporate_events; DELETE FROM asset_logos; DELETE FROM assets;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
