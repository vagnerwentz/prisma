using FluentValidation;

namespace Prisma.Api.Features.Recurrences;

public static class RecurrencesModule
{
    public const string RunnerEnabledKey = "Recurrences:Runner:Enabled";

    public static IServiceCollection AddRecurrenceFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<RecurrenceRunner>();
        services.AddScoped<CreateRecurrence.Handler>();
        services.AddScoped<ListRecurrences.Handler>();
        services.AddScoped<UpdateRecurrence.Handler>();
        services.AddScoped<EndRecurrence.Handler>();
        services.AddScoped<LaunchRecurrencePending.Handler>();
        services.AddScoped<DiscardRecurrencePending.Handler>();

        services.AddSingleton<IValidator<RecurrenceRequest>, CreateRecurrence.Validator>();
        services.AddSingleton<IValidator<UpdateRecurrence.Request>, UpdateRecurrence.Validator>();
        services.AddSingleton<IValidator<LaunchRecurrencePending.Request>, LaunchRecurrencePending.Validator>();

        // Liga por padrão. Os testes de integração desligam: a execução passa por todos os usuários do
        // banco compartilhado, e cada teste chama o gerador quando quer.
        if (configuration.GetValue(RunnerEnabledKey, defaultValue: true))
            services.AddHostedService<RecurrenceWorker>();
        return services;
    }

    public static void MapRecurrenceEndpoints(this IEndpointRouteBuilder app)
    {
        CreateRecurrence.Map(app);

        var group = app.MapGroup("/recurrences");
        ListRecurrences.Map(group);
        UpdateRecurrence.Map(group);
        EndRecurrence.Map(group);
        LaunchRecurrencePending.Map(group);
        DiscardRecurrencePending.Map(group);
    }
}
