using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prisma.Api.Features.Recurrences;
using Prisma.Api.Infrastructure;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Api.Tests.Features;

// docs/fase-2.md, 2.14 (etapa 2.25, tarefa 4): a tarefa que roda o gerador. Sem banco: o que se prova aqui
// é o serviço (liga, desliga, sobrevive a falha). A geração ao subir a API está em RecurrenceRunnerTests.
public sealed class RecurrenceWorkerTests
{
    private static IServiceCollection Features(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ServiceCollection().AddRecurrenceFeatures(configuration);
    }

    private static bool HasWorker(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RecurrenceWorker));

    [Fact]
    public void It_is_on_by_default() => HasWorker(Features()).ShouldBeTrue();

    [Fact]
    public void It_turns_off_by_configuration() =>
        HasWorker(Features((RecurrencesModule.RunnerEnabledKey, "false"))).ShouldBeFalse();

    // O banco fora do ar: cada execução falha, vai para o log, e a seguinte tenta de novo. O serviço não
    // termina com erro, o que pararia a API.
    [Fact]
    public async Task A_failing_run_is_logged_and_the_next_one_still_happens()
    {
        var logs = new LogSink();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton<IClock>(new FakeClock(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc)))
            .AddScoped<AppDbContext>(_ => throw new InvalidOperationException("banco fora do ar"))
            .AddSingleton<RecurrenceRunner>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new RecurrenceWorker(provider.GetRequiredService<RecurrenceRunner>(),
            provider.GetRequiredService<ILogger<RecurrenceWorker>>(), TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await logs.WaitFor(_ => logs.Entries.Count(e => e.EventId.Name == "RecurrenceRunFailed") >= 2);
        await worker.StopAsync(CancellationToken.None);

        var execution = worker.ExecuteTask!;
        execution.IsCompleted.ShouldBeTrue();
        execution.IsFaulted.ShouldBeFalse();
        logs.Entries.ShouldContain(e => e.EventId.Name == "RecurrenceWorkerStarted");
        logs.Entries.First(e => e.EventId.Name == "RecurrenceRunFailed").Exception.ShouldBeOfType<InvalidOperationException>();
    }
}
