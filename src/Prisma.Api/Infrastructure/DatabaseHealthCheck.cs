using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Prisma.Api.Infrastructure;

// Verifica a conexão real com o Postgres, para que /health reflita o estado do banco.
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Banco de dados indisponível.");
}
