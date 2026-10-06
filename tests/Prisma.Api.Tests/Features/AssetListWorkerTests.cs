using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prisma.Api.Features.Assets;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Market;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Api.Tests.Market;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// A tarefa diária do catálogo de ativos e a agenda dela. Sem banco: o que se prova aqui é quando roda, que
// liga e desliga, e que sobrevive a falha. A sincronização em si, e a brapi fora do ar na subida, estão em
// AssetListSyncTests.
public sealed class AssetListWorkerTests
{
    private static DateTime SaoPaulo(int day, int hour, int minute = 0) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc).AddHours(3);

    [Theory]
    [InlineData(4, 3, 30, 4, 4)] // de madrugada, antes da hora: hoje
    [InlineData(4, 4, 0, 5, 4)] // na hora exata: amanhã (acabou de rodar)
    [InlineData(4, 12, 0, 5, 4)]
    [InlineData(4, 23, 30, 5, 4)] // 23h30 em São Paulo já é dia 5 em UTC
    public void Next_run_is_the_next_4am_in_sao_paulo(int day, int hour, int minute, int expectedDay, int expectedHour) =>
        AssetSyncSchedule.NextRun(SaoPaulo(day, hour, minute), 4).ShouldBe(SaoPaulo(expectedDay, expectedHour));

    [Fact]
    public void Next_run_crosses_the_month()
    {
        var lastNight = new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc); // 31/10, 23h em São Paulo

        AssetSyncSchedule.NextRun(lastNight, 4).ShouldBe(new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Next_run_requires_utc() =>
        Should.Throw<ArgumentException>(() =>
            AssetSyncSchedule.NextRun(new DateTime(2026, 10, 4, 4, 0, 0, DateTimeKind.Local), 4));

    [Fact]
    public void It_is_on_by_default() => HasWorker(Features()).ShouldBeTrue();

    [Fact]
    public void It_turns_off_by_configuration() =>
        HasWorker(Features(("Assets:Sync:Enabled", "false"))).ShouldBeFalse();

    [Theory]
    [InlineData("-1")]
    [InlineData("24")]
    public void Invalid_hour_is_rejected(string hour)
    {
        using var provider = Features(("Assets:Sync:HourOfDay", hour)).BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AssetSyncOptions>>().Value);
    }

    // O banco fora do ar: erro no log, e a tarefa segue.
    [Fact]
    public async Task Unexpected_failure_is_an_error_and_the_worker_keeps_going()
    {
        var (worker, logs, provider) = Worker();
        await using var _ = provider;

        await worker.StartAsync(CancellationToken.None);
        await logs.WaitFor(e => e.EventId.Name == "AssetListNextRun");
        await worker.StopAsync(CancellationToken.None);

        logs.Entries.Single(e => e.EventId.Name == "AssetListSyncFailed").Level.ShouldBe(LogLevel.Error);
        // Os logos tentam mesmo assim, e a falha deles também só vai para o log.
        logs.Entries.Single(e => e.EventId.Name == "AssetLogoSyncFailed").Level.ShouldBe(LogLevel.Error);
        worker.ExecuteTask!.IsCompleted.ShouldBeTrue();
        worker.ExecuteTask.IsFaulted.ShouldBeFalse();
    }

    private static IServiceCollection Features(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ServiceCollection().AddAssetFeatures(configuration);
    }

    private static bool HasWorker(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AssetListWorker));

    private static (AssetListWorker Worker, LogSink Logs, ServiceProvider Provider) Worker()
    {
        var logs = new LogSink();
        var clock = new FakeClock(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc));
        var provider = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton<IClock>(clock)
            .AddScoped<IAssetListSource>(_ => new FakeAssetListSource())
            .AddScoped<AppDbContext>(_ => throw new InvalidOperationException("banco fora do ar"))
            .AddSingleton<AssetListSync>()
            .AddSingleton<AssetLogoSync>()
            .BuildServiceProvider();

        var worker = new AssetListWorker(provider.GetRequiredService<AssetListSync>(), provider.GetRequiredService<AssetLogoSync>(), clock,
            Options.Create(new AssetSyncOptions()), provider.GetRequiredService<ILogger<AssetListWorker>>());
        return (worker, logs, provider);
    }
}
