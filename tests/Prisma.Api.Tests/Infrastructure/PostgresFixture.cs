using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prisma.Api.Infrastructure;
using Testcontainers.PostgreSql;

namespace Prisma.Api.Tests.Infrastructure;

// Um único Postgres para toda a suíte, com o schema criado pelas migrations reais.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var factory = new PrismaApiFactory(ConnectionString);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Api";
}
