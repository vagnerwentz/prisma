using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Email;

namespace Prisma.Api.Features.Admin;

// O histórico dos disparos, do mais novo ao mais antigo (docs/admin-email.md, seção 4). Sem dado de pessoa: modelo, dias,
// quantas pessoas e quando. Quem pediu fica só no log.
public static class ListEmailCampaigns
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public sealed class Handler(AppDbContext db)
    {
        public async Task<IReadOnlyList<EmailCampaignResponse>> Execute(int? limit, CancellationToken ct)
        {
            var campaigns = await db.EmailCampaigns.AsNoTracking()
                .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                .Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit))
                .ToListAsync(ct);
            return campaigns.Select(Respond).ToList();
        }
    }

    public static EmailCampaignResponse Respond(EmailCampaign campaign) => new(
        campaign.Id, EmailCampaignCatalog.All.First(t => t.Kind == campaign.Kind).Code, campaign.Days, campaign.Recipients, campaign.CreatedAt);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/campaigns", async (int? limit, Handler handler, CancellationToken ct) => Results.Ok(await handler.Execute(limit, ct)))
            .Produces<IReadOnlyList<EmailCampaignResponse>>(200)
            .ProducesProblem(401)
            .ProducesProblem(403);
}
