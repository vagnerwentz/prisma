using FluentValidation;

namespace Prisma.Api.Features.FixedIncome;

// Objetivos (as caixas) e aportes em renda fixa da pessoa (docs/renda-fixa.md). O saldo e o rendimento chegam na R.5.
public static class FixedIncomeModule
{
    public static IServiceCollection AddFixedIncomeFeatures(this IServiceCollection services)
    {
        services.AddScoped<ListGoals.Handler>();
        services.AddScoped<SaveGoal.Handler>();
        services.AddSingleton<IValidator<SaveGoal.Request>, SaveGoal.Validator>();
        services.AddScoped<DeleteGoal.Handler>();
        services.AddScoped<RestoreGoal.Handler>();

        services.AddScoped<ListDeposits.Handler>();
        services.AddScoped<SaveDeposit.Handler>();
        services.AddSingleton<IValidator<SaveDeposit.CreateRequest>, SaveDeposit.CreateValidator>();
        services.AddSingleton<IValidator<SaveDeposit.UpdateRequest>, SaveDeposit.UpdateValidator>();
        services.AddScoped<MoveDeposit.Handler>();
        services.AddScoped<DeleteDeposit.Handler>();
        services.AddScoped<RestoreDeposit.Handler>();
        services.AddScoped<Redemptions.Handler>();
        services.AddSingleton<IValidator<Redemptions.CreateRequest>, Redemptions.CreateValidator>();
        services.AddScoped<RedemptionBatches.Handler>();
        services.AddSingleton<IValidator<RedemptionBatches.CreateRequest>, RedemptionBatches.CreateValidator>();
        services.AddSingleton<IValidator<RedemptionBatches.UndoRequest>, RedemptionBatches.UndoValidator>();

        services.AddScoped<GetBalances.Handler>();
        services.AddScoped<GetEvolution.Handler>();
        return services;
    }

    public static void MapFixedIncomeEndpoints(this IEndpointRouteBuilder app)
    {
        var goals = app.MapGroup("/savings-goals");
        ListGoals.Map(goals);
        SaveGoal.MapCreate(goals);
        SaveGoal.MapUpdate(goals);
        DeleteGoal.Map(goals);
        RestoreGoal.Map(goals);

        var deposits = app.MapGroup("/fixed-income-deposits");
        ListDeposits.Map(deposits);
        SaveDeposit.MapCreate(deposits);
        SaveDeposit.MapUpdate(deposits);
        MoveDeposit.Map(deposits);
        DeleteDeposit.Map(deposits);
        RestoreDeposit.Map(deposits);
        Redemptions.Map(deposits);
        RedemptionBatches.Map(deposits);

        var fixedIncome = app.MapGroup("/fixed-income");
        GetBalances.Map(fixedIncome);
        GetEvolution.Map(fixedIncome);
    }
}
