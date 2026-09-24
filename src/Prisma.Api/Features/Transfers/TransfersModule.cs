using FluentValidation;

namespace Prisma.Api.Features.Transfers;

public static class TransfersModule
{
    public static IServiceCollection AddTransferFeatures(this IServiceCollection services)
    {
        services.AddScoped<CreateTransfer.Handler>();
        services.AddSingleton<IValidator<CreateTransfer.Request>, CreateTransfer.Validator>();
        return services;
    }

    public static void MapTransferEndpoints(this IEndpointRouteBuilder app) => CreateTransfer.Map(app);
}
