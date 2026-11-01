using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;
using Prisma.Domain.Accounts;

namespace Prisma.Api.Features.Admin;

// Quantas pessoas estão no público de um modelo agora, com o teto por disparo (docs/admin-email.md, seção 4). Só a contagem: o
// dono nunca vê quem são. É esse número que ele confirma no envio.
public static class GetEmailAudience
{
    public sealed class Handler(CampaignAudienceQuery audience, IOptions<EmailOptions> options)
    {
        public async Task<Result<EmailAudienceResponse>> Execute(string? template, int? days, CancellationToken ct)
        {
            if (EmailCampaignCatalog.Find(template) is not { } found)
                return EmailCampaignCatalog.UnknownTemplate;

            var window = ActivityWindow.Of(days ?? found.DefaultDays);
            if (!window.IsSuccess)
                return window.Error;

            var count = await audience.Eligible(found, window.Value).CountAsync(ct);
            return new EmailAudienceResponse(found.Code, window.Value.Days, count, options.Value.Campaigns.MaxRecipients);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/audience", async (string? template, int? days, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(template, days, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<EmailAudienceResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);
}
