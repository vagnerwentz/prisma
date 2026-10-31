using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Features.Admin;

// Superfície de admin (docs/admin-mercado.md; docs/admin-email.md): toda rota sob /admin leva a política `Admin`, no grupo, e o teste
// AdminRoutesTests recusa uma rota /admin que não a tenha.
public static class AdminModule
{
    public static IServiceCollection AddAdminFeatures(this IServiceCollection services)
    {
        services.AddScoped<GetMarketData.Handler>();
        services.AddScoped<SyncMarketData.Handler>();
        services.AddScoped<ListSyncRuns.Handler>();
        services.AddScoped<GetUserSummary.Handler>();

        // Disparos de e-mail (docs/admin-email.md, seção 4): os casos de uso e os montadores dos modelos.
        services.AddScoped<CampaignAudienceQuery>();
        services.AddScoped<ListEmailTemplates.Handler>();
        services.AddScoped<GetEmailAudience.Handler>();
        services.AddScoped<PreviewEmail.Handler>();
        services.AddScoped<SendEmailTest.Handler>();
        services.AddScoped<SendEmailCampaign.Handler>();
        services.AddScoped<ListEmailCampaigns.Handler>();
        services.AddScoped<IEmailComposer, ConfirmEmailReminderComposer>();
        services.AddScoped<IEmailComposer, MissYouComposer>();
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

        // Pessoas (docs/admin-email.md, AE.2): só contagens.
        GetUserSummary.Map(admin.MapGroup("/users"));

        // E-mails: modelos, público, prévia, teste, disparo e histórico (docs/admin-email.md, seção 4).
        var emails = admin.MapGroup("/emails");
        ListEmailTemplates.Map(emails);
        GetEmailAudience.Map(emails);
        PreviewEmail.Map(emails);
        SendEmailTest.Map(emails);
        SendEmailCampaign.Map(emails);
        ListEmailCampaigns.Map(emails);
    }
}
