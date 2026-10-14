namespace Prisma.Api.Infrastructure.Privacy;

// Apagar a conta e apagar de vez o excluído (etapa A.3).
public static class PrivacySetup
{
    public const string ErasureEnabledKey = "Privacy:Erasure:Enabled";

    public static IServiceCollection AddPrivacy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AccountEraser>();
        services.AddSingleton<ErasureRunner>();

        // Liga por padrão. Os testes de integração desligam: a limpeza passa pelo banco compartilhado inteiro, e cada
        // teste a chama quando quer.
        if (configuration.GetValue(ErasureEnabledKey, defaultValue: true))
            services.AddHostedService<ErasureWorker>();
        return services;
    }
}
