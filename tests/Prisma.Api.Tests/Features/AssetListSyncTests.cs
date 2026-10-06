using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Configurations;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain.Market;
using Shouldly;
using static Prisma.Api.Tests.Market.FakeAssetListSource;

namespace Prisma.Api.Tests.Features;

// docs/investimentos.md, seção 8, etapa 2: a sincronização do catálogo de ativos contra o Postgres, com a
// fonte falsa no lugar da brapi. O catálogo é global, então cada teste começa com a tabela vazia.
[Collection(ApiCollection.Name)]
public sealed class AssetListSyncTests(PostgresFixture postgres)
{
    private static readonly DateTime Day1 = new(2026, 10, 4, 7, 0, 0, DateTimeKind.Utc);

    private readonly FakeAssetListSource _source = new();
    private readonly FakeClock _clock = new(Day1);
    private readonly LogSink _logs = new();
    private readonly FakeAssetLogoDownloader _logos = new();

    [Fact]
    public async Task First_sync_fills_the_catalog_and_running_again_changes_nothing()
    {
        await using var factory = await Factory();
        _source.Assets = [Listed("BBAS3"), Listed("TAEE11", AssetKind.Unit), Listed("MXRF11", AssetKind.Fii, "MXRF11", "Maxi Renda FII")];

        await Sync(factory);
        var first = await Catalog(factory);
        _clock.UtcNow = Day1.AddDays(1);
        var again = await Sync(factory);

        first.Select(a => (a.Symbol, a.Kind)).Order().ShouldBe(
            [("BBAS3", AssetKind.Stock), ("MXRF11", AssetKind.Fii), ("TAEE11", AssetKind.Unit)]);
        first.Single(a => a.Symbol == "MXRF11").LongName.ShouldBe("Maxi Renda FII");
        (again.Added.Count, again.Updated, again.Deactivated).ShouldBe((0, 0, 0));
        var second = await Catalog(factory);
        second.Select(a => (a.Id, a.UpdatedAt)).Order().ShouldBe(first.Select(a => (a.Id, a.UpdatedAt)).Order());
    }

    [Fact]
    public async Task Asset_that_leaves_the_list_stays_inactive_and_comes_back_with_the_same_id()
    {
        await using var factory = await Factory();
        _source.Assets = [Listed("BBAS3"), Listed("ITSA4")];
        await Sync(factory);
        var id = (await Catalog(factory)).Single(a => a.Symbol == "ITSA4").Id;

        _clock.UtcNow = Day1.AddDays(1);
        _source.Assets = [Listed("BBAS3")];
        await Sync(factory);
        var gone = (await Catalog(factory)).Single(a => a.Symbol == "ITSA4");

        _clock.UtcNow = Day1.AddDays(2);
        _source.Assets = [Listed("BBAS3"), Listed("ITSA4")];
        await Sync(factory);
        var back = (await Catalog(factory)).Single(a => a.Symbol == "ITSA4");

        gone.IsActive.ShouldBeFalse();
        gone.InactiveSince.ShouldBe(Day1.AddDays(1));
        back.Id.ShouldBe(id);
        back.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Changed_name_updates_the_existing_row()
    {
        await using var factory = await Factory();
        _source.Assets = [Listed("BBAS3", name: "BCO BRASIL S.A.")];
        await Sync(factory);

        _clock.UtcNow = Day1.AddDays(1);
        _source.Assets = [Listed("BBAS3", name: "BANCO DO BRASIL S.A.")];
        var changes = await Sync(factory);

        changes.Updated.ShouldBe(1);
        var asset = (await Catalog(factory)).Single();
        asset.Name.ShouldBe("BANCO DO BRASIL S.A.");
        asset.UpdatedAt.ShouldBe(Day1.AddDays(1));
        asset.CreatedAt.ShouldBe(Day1);
    }

    [Fact]
    public async Task Shrunken_list_is_refused_and_nothing_is_written()
    {
        await using var factory = await Factory();
        _source.Assets = [Listed("AAAA3"), Listed("BBBB3"), Listed("CCCC3"), Listed("DDDD3")];
        await Sync(factory);

        _clock.UtcNow = Day1.AddDays(1);
        _source.Assets = [Listed("AAAA3")];
        var changes = await Sync(factory);

        changes.IsRefused.ShouldBeTrue();
        (await Catalog(factory)).ShouldAllBe(a => a.IsActive && a.UpdatedAt == Day1);
        var warning = _logs.Entries.Single(e => e.EventId.Name == "AssetListSyncRefused");
        (warning["ActiveBefore"], warning["Listed"]).ShouldBe((4, 1));
    }

    [Fact]
    public async Task Provider_failure_leaves_the_catalog_as_it_was()
    {
        await using var factory = await Factory();
        _source.Assets = [Listed("BBAS3")];
        await Sync(factory);

        _source.Failure = new MarketDataException("brapi tickers page 2: HTTP 502");
        await Should.ThrowAsync<MarketDataException>(() => Sync(factory));

        (await Catalog(factory)).Single().IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task The_database_keeps_one_row_per_symbol()
    {
        // A garantia final contra duas sincronizações ao mesmo tempo: o índice único, não o código.
        await using var factory = await Factory();
        _source.Assets = [Listed("BBAS3")];
        await Sync(factory);
        var duplicate = AssetListReconciliation.Apply([], [Listed("BBAS3")], Day1).Added.Single();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Assets.Add(duplicate);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        error.IsUniqueViolation(AssetConfiguration.SymbolIndex).ShouldBeTrue();
    }

    [Theory]
    [InlineData("BBAS3", "Crypto")] // tipo que o AssetKind não conhece
    [InlineData("bbas3", "Stock")] // código fora do padrão
    public async Task The_database_rejects_rows_outside_the_rules(string symbol, string kind)
    {
        await using var factory = await Factory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO assets (id, symbol, name, kind, search_text, created_at, updated_at) VALUES ({Guid.CreateVersion7()}, {symbol}, 'Nome', {kind}, 'nome', now(), now())"));

        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_worker_fills_an_empty_catalog_when_the_api_starts()
    {
        await ClearCatalog();
        _source.Assets = [Listed("BBAS3"), Listed("MXRF11", AssetKind.Fii)];

        await using var factory = await Factory(workerEnabled: true);
        _ = factory.Services; // sobe a API, e com ela a tarefa
        await _logs.WaitFor(e => e.EventId.Name == "AssetListSynced");

        (await Catalog(factory)).Count.ShouldBe(2);
        _logs.Entries.ShouldContain(e => e.EventId.Name == "AssetListNextRun");
    }

    // Etapa 4: o catálogo já existia, mas nenhum logo foi guardado ainda (a primeira subida com logos).
    [Fact]
    public async Task The_worker_runs_at_startup_when_no_logo_was_kept_yet()
    {
        await using (var seeding = await Factory())
        {
            _source.Assets = [Listed("BBAS3", logoUrl: "https://icons.example/BBAS3.svg")];
            await Sync(seeding);
        }

        _logos.Svgs["https://icons.example/BBAS3.svg"] = FakeAssetLogoDownloader.Square("#FFF22D");
        await using var factory = await Factory(workerEnabled: true, clear: false);
        _ = factory.Services;
        await _logs.WaitFor(e => e.EventId.Name == "AssetLogosSynced");

        _logs.Entries.Single(e => e.EventId.Name == "AssetLogosSynced")["Saved"].ShouldBe(1);
    }

    [Fact]
    public async Task Provider_down_at_startup_is_a_warning_and_the_worker_keeps_its_schedule()
    {
        await ClearCatalog();
        _source.Failure = new MarketDataException("brapi tickers page 1: HTTP 503");

        await using var factory = await Factory(workerEnabled: true);
        _ = factory.Services;
        await _logs.WaitFor(e => e.EventId.Name == "AssetListNextRun");

        _logs.Entries.Single(e => e.EventId.Name == "AssetListSyncUnavailable").Level
            .ShouldBe(Microsoft.Extensions.Logging.LogLevel.Warning);
        (await Catalog(factory)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_worker_waits_for_the_schedule_when_the_catalog_has_assets_and_logos()
    {
        await using (var seeding = await Factory())
        {
            _source.Assets = [Listed("BBAS3", logoUrl: "https://icons.example/BBAS3.svg")];
            _logos.Svgs["https://icons.example/BBAS3.svg"] = FakeAssetLogoDownloader.Square("#FFF22D");
            await Sync(seeding);
            await seeding.Services.GetRequiredService<AssetLogoSync>().RunAsync(CancellationToken.None);
        }

        var callsBefore = _source.Calls;
        await using var factory = await Factory(workerEnabled: true, clear: false);
        _ = factory.Services;
        await _logs.WaitFor(e => e.EventId.Name == "AssetListNextRun");

        _source.Calls.ShouldBe(callsBefore);
    }

    private async Task<PrismaApiFactory> Factory(bool workerEnabled = false, bool clear = true)
    {
        if (clear)
            await ClearCatalog();

        return new PrismaApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string> { ["Assets:Sync:Enabled"] = workerEnabled ? "true" : "false" },
            _clock,
            _logs,
            services =>
            {
                services.AddScoped<IAssetListSource>(_ => _source);
                services.AddScoped<IAssetLogoDownloader>(_ => _logos);
            });
    }

    private Task ClearCatalog() => MarketCatalog.ClearAsync(postgres.ConnectionString);

    private static Task<AssetListChanges> Sync(PrismaApiFactory factory) =>
        factory.Services.GetRequiredService<AssetListSync>().RunAsync(CancellationToken.None);

    private static async Task<List<Asset>> Catalog(PrismaApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assets.AsNoTracking().ToListAsync();
    }
}
