using FluentValidation;

namespace Prisma.Api.Features.Diagnostics;

public static class DiagnosticsModule
{
    public static IServiceCollection AddDiagnosticsFeatures(this IServiceCollection services)
    {
        services.AddScoped<ReportClientError.Handler>();
        services.AddSingleton<IValidator<ReportClientError.Request>, ReportClientError.Validator>();
        return services;
    }

    public static void MapDiagnosticsEndpoints(this IEndpointRouteBuilder app) => ReportClientError.Map(app);
}
