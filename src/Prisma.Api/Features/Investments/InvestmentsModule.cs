using FluentValidation;

namespace Prisma.Api.Features.Investments;

// Investimentos da pessoa (docs/investimentos.md): a carteira, os proventos, as notas e os avisos de ativo faltando.
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
        services.AddScoped<GetPortfolio.Handler>();

        services.AddScoped<ListAssetRequests.Handler>();
        services.AddScoped<RequestAsset.Handler>();
        services.AddScoped<RemoveAssetRequest.Handler>();
        services.AddScoped<RestoreAssetRequest.Handler>();

        services.AddScoped<ListEventReviews.Handler>();
        services.AddScoped<ConfirmEvents.Handler>();
        services.AddSingleton<IValidator<ConfirmEvents.Request>, ConfirmEvents.Validator>();
        services.AddScoped<DisputeEvent.Handler>();
        services.AddScoped<UndoEventReview.Handler>();
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
        GetPortfolio.Map(app.MapGroup("/portfolio"));

        var requests = app.MapGroup("/asset-requests");
        ListAssetRequests.Map(requests);
        RequestAsset.Map(requests);
        RemoveAssetRequest.Map(requests);
        RestoreAssetRequest.Map(requests);

        var reviews = app.MapGroup("/event-reviews");
        ListEventReviews.Map(reviews);
        ConfirmEvents.Map(reviews);
        DisputeEvent.Map(reviews);
        UndoEventReview.Map(reviews);
    }
}
