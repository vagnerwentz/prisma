using System.Net;
using Prisma.Api.Tests.Infrastructure;
using Shouldly;

namespace Prisma.Api.Tests;

public sealed class HealthTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Health_returns_200_when_database_is_reachable()
    {
        await using var factory = new PrismaApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_returns_503_when_database_is_unreachable()
    {
        // Porta 1 não tem Postgres escutando: prova que /health consulta o banco de fato.
        const string unreachable = "Host=localhost;Port=1;Database=prisma;Username=prisma;Password=x;Timeout=2";
        await using var factory = new PrismaApiFactory(unreachable);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
