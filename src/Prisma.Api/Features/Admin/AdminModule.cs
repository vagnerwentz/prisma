using Prisma.Api.Infrastructure.Auth;

namespace Prisma.Api.Features.Admin;

// Superfície de admin (docs/admin-mercado.md): toda rota sob /admin leva a política `Admin`, no grupo, e o teste
// AdminRoutesTests recusa uma rota /admin que não a tenha.
public static class AdminModule
{
    public static IServiceCollection AddAdminFeatures(this IServiceCollection services)
    {
        services.AddScoped<GetMarketData.Handler>();
        services.AddScoped<SyncMarketData.Handler>();
        services.AddScoped<ListSyncRuns.Handler>();
        return services;
    }

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization(AdminOptions.PolicyName);

        var marketData = admin.MapGroup("/market-data");
        GetMarketData.Map(marketData);
        SyncMarketData.MapSync(marketData);
        SyncMarketData.MapRefresh(marketData);
        ListSyncRuns.Map(marketData);
    }
}
