using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Email.Resend;

namespace Prisma.Api.Infrastructure.Email;

// Envio de e-mail (docs/email.md, seção 2): a fila, a tarefa que a esvazia e o fornecedor escolhido por Email:Transport.
// Os tipos de e-mail entram como IEmailComposer, cada um com o fluxo que o pede.
public static class EmailSetup
{
    public static IServiceCollection AddEmail(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.Section))
            .Validate(o => o.IsValid(environment.IsDevelopment()),
                "Configuração Email inválida: confira From, ReplyTo e a chave do Resend (Email:Resend:ApiKey). " +
                "Fora de desenvolvimento, Email:Transport precisa ser Resend.")
            .ValidateOnStart();

        var transport = configuration.GetValue($"{EmailOptions.Section}:Transport", EmailTransportKind.Resend);
        if (transport == EmailTransportKind.Folder)
            services.AddSingleton<IEmailTransport, FolderEmailTransport>();
        else
            services.AddHttpClient<IEmailTransport, ResendEmailTransport>(ResendEmailTransport.HttpClientName, (provider, client) =>
            {
                var resend = provider.GetRequiredService<IOptions<EmailOptions>>().Value.Resend;
                client.BaseAddress = resend.BaseUrl;
                client.Timeout = TimeSpan.FromSeconds(resend.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Prisma", "1.0"));
                if (!string.IsNullOrWhiteSpace(resend.ApiKey))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resend.ApiKey);
            });

        services.AddScoped<EmailQueue>();
        services.AddSingleton<EmailOutboxProcessor>();

        // Liga por padrão. Os testes de integração desligam: a fila do banco compartilhado é de todos os testes, e cada
        // um chama o processador quando quer.
        if (configuration.GetValue(OutboxOptions.EnabledKey, defaultValue: true))
            services.AddHostedService<EmailOutboxWorker>();
        return services;
    }
}
