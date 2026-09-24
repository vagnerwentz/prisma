using Prisma.Api.Features.InstallmentPurchases;

namespace Prisma.Api.Features.Statements;

// Endpoints do cartão que não pertencem a /transactions: faturas e compras parceladas.
public static class CardModule
{
    public static IServiceCollection AddCardFeatures(this IServiceCollection services)
    {
        services.AddScoped<ListStatements.Handler>();
        services.AddScoped<UpdateStatement.Handler>();
        services.AddScoped<DeleteInstallmentPurchase.Handler>();
        services.AddScoped<UpdateInstallmentPurchase.Handler>();
        services.AddScoped<RestoreInstallmentPurchase.Handler>();
        return services;
    }

    public static void MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        ListStatements.Map(app);
        UpdateStatement.Map(app);
        DeleteInstallmentPurchase.Map(app);
        UpdateInstallmentPurchase.Map(app);
        RestoreInstallmentPurchase.Map(app);
    }
}
