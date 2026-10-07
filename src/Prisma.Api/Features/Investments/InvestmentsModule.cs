using FluentValidation;

namespace Prisma.Api.Features.Investments;

// Investimentos da pessoa (docs/investimentos.md): a carteira, os proventos e as notas.
public static class InvestmentsModule
{
    public static IServiceCollection AddInvestmentFeatures(this IServiceCollection services)
    {
        services.AddScoped<ListHoldings.Handler>();
        services.AddScoped<AddHolding.Handler>();
        services.AddScoped<RemoveHolding.Handler>();
        services.AddSingleton<IValidator<AddHolding.Request>, AddHolding.Validator>();

        services.AddScoped<ListPayouts.Handler>();
        services.AddScoped<SavePayout.Handler>();
        services.AddSingleton<IValidator<SavePayout.Request>, SavePayout.Validator>();

        services.AddScoped<ListTradeNotes.Handler>();
        services.AddScoped<SaveTradeNote.Handler>();
        services.AddScoped<DeleteTradeNote.Handler>();
        services.AddScoped<RestoreTradeNote.Handler>();
        services.AddSingleton<IValidator<SaveTradeNote.Request>, SaveTradeNote.Validator>();
        services.AddScoped<ListPositions.Handler>();
        return services;
    }

    public static void MapInvestmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/holdings");
        ListHoldings.Map(group);
        AddHolding.Map(group);
        RemoveHolding.Map(group);

        var payouts = app.MapGroup("/payouts");
        ListPayouts.Map(payouts);
        SavePayout.MapCreate(payouts);
        SavePayout.MapUpdate(payouts);

        var notes = app.MapGroup("/trade-notes");
        ListTradeNotes.Map(notes);
        SaveTradeNote.MapCreate(notes);
        SaveTradeNote.MapUpdate(notes);
        DeleteTradeNote.Map(notes);
        RestoreTradeNote.Map(notes);

        ListPositions.Map(app.MapGroup("/positions"));
    }
}
