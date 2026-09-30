using FluentValidation;

namespace Prisma.Api.Features.Transactions;

public static class TransactionsModule
{
    public static IServiceCollection AddTransactionFeatures(this IServiceCollection services)
    {
        services.AddScoped<ListTransactions.Handler>();
        services.AddScoped<GetTransaction.Handler>();
        services.AddScoped<CreateTransaction.Handler>();
        services.AddScoped<UpdateTransaction.Handler>();
        services.AddScoped<DeleteTransaction.Handler>();
        services.AddScoped<RestoreTransaction.Handler>();
        services.AddScoped<ListDescriptions.Handler>();
        services.AddScoped<MoveStatement.Handler>();
        services.AddScoped<ListToConfirm.Handler>();
        services.AddScoped<ConfirmAmount.Handler>();

        services.AddSingleton<IValidator<CreateTransaction.Request>, CreateTransaction.Validator>();
        services.AddSingleton<IValidator<UpdateTransaction.Request>, UpdateTransaction.Validator>();
        services.AddSingleton<IValidator<MoveStatement.Request>, MoveStatement.Validator>();

        return services;
    }

    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transactions");
        ListTransactions.Map(group);
        GetTransaction.Map(group);
        CreateTransaction.Map(group);
        UpdateTransaction.Map(group);
        DeleteTransaction.Map(group);
        RestoreTransaction.Map(group);
        ListDescriptions.Map(group);
        MoveStatement.Map(group);
        ListToConfirm.Map(group);
        ConfirmAmount.Map(group);
    }
}
