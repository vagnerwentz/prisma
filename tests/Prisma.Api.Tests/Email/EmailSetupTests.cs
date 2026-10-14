using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Email.Resend;
using Prisma.Api.Tests.Infrastructure;
using Prisma.Domain;
using Shouldly;

namespace Prisma.Api.Tests.Email;

// docs/email.md, seção 2: qual fornecedor, com que configuração, e a API que não sobe sem a chave fora de
// desenvolvimento. Sem banco.
public sealed class EmailSetupTests
{
    private const string NoDatabase = "Host=localhost;Port=1;Database=prisma;Username=prisma;Password=x;Timeout=2";

    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["Email:From"] = "Prisma Money <nao-responda@prismamoney.com.br>",
        ["Email:ReplyTo"] = "contato@prismamoney.com.br",
        ["Email:Resend:ApiKey"] = "re_teste",
    };

    private static ServiceProvider Build(string environment, params (string Key, string? Value)[] settings)
    {
        var values = new Dictionary<string, string?>(Valid);
        foreach (var (key, value) in settings)
            values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var host = new HostingEnvironment { EnvironmentName = environment, ContentRootPath = Path.GetTempPath() };

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IClock>(new FakeClock(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc)))
            .AddSingleton<IHostEnvironment>(host)
            .AddEmail(configuration, host)
            .BuildServiceProvider();
    }

    private static EmailOptions OptionsOf(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<EmailOptions>>().Value;

    [Fact]
    public void Production_sends_through_resend_with_the_configured_client()
    {
        using var provider = Build(Environments.Production,
            ("Email:Resend:BaseUrl", "https://api.resend.exemplo/"), ("Email:Resend:TimeoutSeconds", "9"));

        provider.GetRequiredService<IEmailTransport>().ShouldBeOfType<ResendEmailTransport>();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ResendEmailTransport.HttpClientName);
        client.BaseAddress.ShouldBe(new Uri("https://api.resend.exemplo/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(9));
        // No cabeçalho, nunca na URL: a URL aparece nos logs do HttpClient.
        client.DefaultRequestHeaders.Authorization!.Scheme.ShouldBe("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.ShouldBe("re_teste");
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldStartWith("Prisma");
    }

    [Fact]
    public void Development_can_write_to_a_folder_without_a_key()
    {
        using var provider = Build(Environments.Development, ("Email:Transport", "Folder"), ("Email:Resend:ApiKey", null));

        provider.GetRequiredService<IEmailTransport>().ShouldBeOfType<FolderEmailTransport>();
        OptionsOf(provider).Folder.ShouldBe(".dev-emails");
    }

    [Theory]
    [InlineData("Email:Resend:ApiKey", null)]
    [InlineData("Email:Resend:ApiKey", " ")]
    [InlineData("Email:Transport", "Folder")] // pasta local só em desenvolvimento
    [InlineData("Email:From", "")]
    [InlineData("Email:From", "nao é endereço")]
    [InlineData("Email:ReplyTo", "")]
    [InlineData("Email:Resend:TimeoutSeconds", "0")]
    [InlineData("Email:Outbox:PollSeconds", "0")]
    [InlineData("Email:Outbox:BatchSize", "0")]
    public void Invalid_configuration_is_refused_outside_development(string key, string? value)
    {
        using var provider = Build(Environments.Production, (key, value));

        Should.Throw<OptionsValidationException>(() => OptionsOf(provider));
    }

    [Fact]
    public async Task The_api_does_not_start_in_production_without_the_key()
    {
        await using var factory = new PrismaApiFactory(NoDatabase, new Dictionary<string, string>
        {
            ["Email:Resend:ApiKey"] = "",
        });
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        var error = Should.Throw<OptionsValidationException>(() => production.Services);
        error.Message.ShouldContain("Email:Resend:ApiKey");
    }

    [Fact]
    public void The_repository_configuration_sends_from_no_reply_and_replies_to_contact()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(AppContext.BaseDirectory))
            .AddJsonFile("appsettings.json")
            .Build();
        var email = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>()!;

        email.Transport.ShouldBe(EmailTransportKind.Resend);
        email.From.ShouldBe("Prisma Money <nao-responda@prismamoney.com.br>");
        email.ReplyTo.ShouldBe("contato@prismamoney.com.br");
        email.Resend.ApiKey.ShouldBeNull("A chave nunca vai no repositório.");
    }

    [Fact]
    public void The_worker_is_on_by_default() => HasWorker(Build(Environments.Production)).ShouldBeTrue();

    [Fact]
    public void The_worker_turns_off_by_configuration() =>
        HasWorker(Build(Environments.Production, (OutboxOptions.EnabledKey, "false"))).ShouldBeFalse();

    private static bool HasWorker(ServiceProvider provider)
    {
        using (provider)
            return provider.GetServices<IHostedService>().OfType<EmailOutboxWorker>().Any();
    }

    // O banco fora do ar: cada volta falha, vai para o log, e a seguinte tenta de novo. O serviço não termina com erro,
    // o que pararia a API.
    [Fact]
    public async Task A_failing_run_is_logged_and_the_next_one_still_happens()
    {
        var logs = new LogSink();
        var host = new HostingEnvironment { EnvironmentName = Environments.Production };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(Valid) { ["Email:Outbox:PollSeconds"] = "1" })
            .Build();
        await using var provider = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddSingleton<IClock>(new FakeClock(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc)))
            .AddEmail(configuration, host)
            .AddScoped<AppDbContext>(_ => throw new InvalidOperationException("banco fora do ar"))
            .BuildServiceProvider();
        var worker = provider.GetServices<IHostedService>().OfType<EmailOutboxWorker>().Single();

        await worker.StartAsync(CancellationToken.None);
        await logs.WaitFor(_ => logs.Entries.Count(e => e.EventId.Name == "EmailOutboxRunFailed") >= 2);
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompleted.ShouldBeTrue();
        worker.ExecuteTask.IsFaulted.ShouldBeFalse();
        logs.Entries.ShouldContain(e => e.EventId.Name == "EmailOutboxWorkerStarted");
        logs.Entries.First(e => e.EventId.Name == "EmailOutboxRunFailed").Exception.ShouldBeOfType<InvalidOperationException>();
    }
}
