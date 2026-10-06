using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Infrastructure;
using Prisma.Domain.Investments;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, seção 8, etapa 5a: a carteira (os ativos que a pessoa tem), contra o Postgres.
[Collection(ApiCollection.Name)]
public sealed class HoldingsTests(PostgresFixture postgres)
{
    private sealed record HoldingDto(Guid Id, Guid AssetId, string Symbol, string Name, string Kind, bool IsActive, DateTime AddedAt);

    private sealed record AssetDto(Guid Id, string Symbol);

    private readonly FakeAssetListSource _source = new();
    private readonly LogSink _logs = new();

    [Fact]
    public async Task Added_asset_shows_in_the_portfolio_in_symbol_order()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        await Add(client, await AssetId(client, "MXRF11"));
        await Add(client, await AssetId(client, "BBAS3"));

        var holdings = await List(client);
        holdings.Select(h => (h.Symbol, h.Kind)).ShouldBe([("BBAS3", "Stock"), ("MXRF11", "Fii")]);
        holdings.Single(h => h.Symbol == "MXRF11").Name.ShouldBe("Maxi Renda FII");
    }

    [Fact]
    public async Task Adding_again_does_not_duplicate()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();
        var bbas = await AssetId(client, "BBAS3");

        var first = await Add(client, bbas);
        var second = await Add(client, bbas);

        second.Id.ShouldBe(first.Id);
        (await List(client)).Count.ShouldBe(1);
        _logs.Entries.Count(e => e.EventId.Name == "HoldingAdded").ShouldBe(1);
    }

    [Fact]
    public async Task Two_adds_at_the_same_time_keep_one()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();
        var bbas = await AssetId(client, "BBAS3");

        // Vários toques quase juntos: o índice único segura, e quem perde a corrida devolve o que o outro criou.
        var both = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Add(client, bbas)));

        both.Select(h => h.Id).Distinct().Count().ShouldBe(1);
        (await List(client)).Count.ShouldBe(1);
    }

    // A corrida sem depender da sorte: outra conexão grava a mesma carteira logo antes do SaveChanges do pedido.
    [Fact]
    public async Task Losing_the_race_returns_what_the_winner_created()
    {
        var racer = new RacingHolding(postgres.ConnectionString);
        await using var factory = await Seeded(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(racer)));
        var client = await factory.CreateAuthenticatedClientAsync();

        var added = await Add(client, await AssetId(client, "BBAS3"));

        added.Id.ShouldBe(racer.WinnerId!.Value);
        (await List(client)).Single().Id.ShouldBe(racer.WinnerId.Value);
    }

    [Fact]
    public async Task Removed_asset_leaves_the_portfolio_and_undo_brings_back_the_same_one()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();
        var added = await Add(client, await AssetId(client, "BBAS3"));

        (await client.DeleteAsync($"/holdings/{added.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await List(client)).ShouldBeEmpty();

        var back = await Add(client, added.AssetId);
        back.Id.ShouldBe(added.Id);
        back.AddedAt.ShouldBe(added.AddedAt);
        (await List(client)).Single().Id.ShouldBe(added.Id);
    }

    [Fact]
    public async Task Asset_that_left_the_exchange_can_be_added_and_is_marked()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();
        var bbas = await AssetId(client, "BBAS3");
        _source.Assets = [Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda FII"), Listed("ITSA4")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);

        var added = await Add(client, bbas);

        added.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_asset_is_not_found()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/holdings", new { assetId = Guid.CreateVersion7() });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_asset_is_a_validation_error()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/holdings", new { assetId = Guid.Empty });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Each_person_sees_and_removes_only_their_own_portfolio()
    {
        await using var factory = await Seeded();
        var ana = await factory.CreateAuthenticatedClientAsync();
        var bia = await factory.CreateAuthenticatedClientAsync();
        var bbas = await AssetId(ana, "BBAS3");
        var anas = await Add(ana, bbas);

        (await List(bia)).ShouldBeEmpty();
        (await bia.DeleteAsync($"/holdings/{anas.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bias = await Add(bia, bbas);

        bias.Id.ShouldNotBe(anas.Id);
        (await List(ana)).Single().Id.ShouldBe(anas.Id);
    }

    [Fact]
    public async Task Removing_twice_is_not_found()
    {
        await using var factory = await Seeded();
        var client = await factory.CreateAuthenticatedClientAsync();
        var added = await Add(client, await AssetId(client, "BBAS3"));
        await client.DeleteAsync($"/holdings/{added.Id}");

        (await client.DeleteAsync($"/holdings/{added.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Requires_login()
    {
        await using var factory = await Seeded();
        var anonymous = factory.CreateHttpsClient();

        (await anonymous.GetAsync("/holdings")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/holdings", new { assetId = Guid.CreateVersion7() })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<PrismaApiFactory> Seeded(Action<IServiceCollection>? more = null)
    {
        await MarketCatalog.ClearAsync(postgres.ConnectionString);
        var factory = new PrismaApiFactory(postgres.ConnectionString, null, null, _logs,
            services =>
            {
                services.AddScoped<IAssetListSource>(_ => _source);
                more?.Invoke(services);
            });
        _source.Assets = [Listed("BBAS3"), Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda FII"), Listed("ITSA4")];
        await factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);
        return factory;
    }

    private static async Task<Guid> AssetId(HttpClient client, string symbol) =>
        (await client.GetFromJsonAsync<List<AssetDto>>($"/assets?q={symbol}"))!.First(a => a.Symbol == symbol).Id;

    private static async Task<HoldingDto> Add(HttpClient client, Guid assetId)
    {
        var response = await client.PostAsJsonAsync("/holdings", new { assetId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<HoldingDto>())!;
    }

    private static async Task<List<HoldingDto>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<HoldingDto>>("/holdings"))!;

    // Na primeira gravação de uma carteira nova, grava antes a mesma carteira por fora, como faria outro pedido.
    private sealed class RacingHolding(string connectionString) : SaveChangesInterceptor
    {
        public Guid? WinnerId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var added = eventData.Context!.ChangeTracker.Entries<Holding>().FirstOrDefault(e => e.State == EntityState.Added);
            if (WinnerId is null && added is not null)
            {
                WinnerId = Guid.CreateVersion7();
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO holdings (id, user_id, asset_id, created_at, updated_at) VALUES (@id, @user, @asset, now(), now())",
                    connection);
                insert.Parameters.AddWithValue("id", WinnerId.Value);
                insert.Parameters.AddWithValue("user", added.Entity.UserId);
                insert.Parameters.AddWithValue("asset", added.Entity.AssetId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
