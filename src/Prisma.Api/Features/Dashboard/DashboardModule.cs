namespace Prisma.Api.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardFeatures(this IServiceCollection services)
    {
        services.AddScoped<GetMonthlySummary.Handler>();
        return services;
    }

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dashboard");
        GetMonthlySummary.Map(group);
    }
}
