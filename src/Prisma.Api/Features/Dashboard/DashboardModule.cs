namespace Prisma.Api.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardFeatures(this IServiceCollection services)
    {
        services.AddScoped<GetMonthlySummary.Handler>();
        services.AddScoped<ListCategoryTotals.Handler>();
        services.AddScoped<GetMonthlyHistory.Handler>();
        services.AddScoped<ListUpcomingStatements.Handler>();
        services.AddScoped<GetInheritedInstallments.Handler>();
        services.AddScoped<GetCommittedMonths.Handler>();
        return services;
    }

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dashboard");
        GetMonthlySummary.Map(group);
        ListCategoryTotals.Map(group);
        GetMonthlyHistory.Map(group);
        ListUpcomingStatements.Map(group);
        GetInheritedInstallments.Map(group);
        GetCommittedMonths.Map(group);
    }
}
