namespace Prisma.Api.Features.Admin;

// Os modelos de disparo e se cada um pode sair agora (docs/admin-email.md, seção 4).
public static class ListEmailTemplates
{
    public sealed class Handler
    {
        public IReadOnlyList<EmailTemplateResponse> Execute() => EmailCampaignCatalog.All.Select(t =>
        {
            var blocked = EmailCampaignCatalog.BlockedCode(t);
            return new EmailTemplateResponse(
                t.Code, t.Name, t.Description, t.Category.ToString(), t.AudienceLabel, t.DefaultDays, t.MinDaysBetween,
                Sendable: blocked is null, blocked);
        }).ToList();
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/templates", (Handler handler) => Results.Ok(handler.Execute()))
            .Produces<IReadOnlyList<EmailTemplateResponse>>(200)
            .ProducesProblem(401)
            .ProducesProblem(403);
}
