using FluentValidation;

namespace Prisma.Api.Features.Accounts;

public static class AccountsModule
{
    public static IServiceCollection AddAccountFeatures(this IServiceCollection services)
    {
        services.AddScoped<CreateAccount.Handler>();
        services.AddScoped<ListAccounts.Handler>();
        services.AddScoped<UpdateAccount.Handler>();
        services.AddScoped<DeleteAccount.Handler>();

        services.AddSingleton<IValidator<CreateAccount.Request>, CreateAccount.Validator>();

        return services;
    }

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts");
        CreateAccount.Map(group);
        ListAccounts.Map(group);
        UpdateAccount.Map(group);
        DeleteAccount.Map(group);
    }
}
