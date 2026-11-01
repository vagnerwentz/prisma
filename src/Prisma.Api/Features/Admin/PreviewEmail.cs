using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Domain;

namespace Prisma.Api.Features.Admin;

// Como o e-mail de um modelo fica, com dados de exemplo (docs/admin-email.md, seção 4): nunca uma pessoa de verdade nem um token
// que valha. Devolve o HTML pronto; a tela o mostra num quadro isolado. Funciona também para modelo bloqueado: ver não é enviar.
public static class PreviewEmail
{
    public sealed record Request(string? Template);

    public sealed class Handler(IOptions<AppOptions> app, IClock clock)
    {
        public Result<EmailPreviewResponse> Execute(Request request)
        {
            if (EmailCampaignCatalog.Find(request.Template) is not { } template)
                return EmailCampaignCatalog.UnknownTemplate;

            var publicUrl = app.Value.PublicUrl;
            var content = EmailLayout.Render(CampaignEmails.Example(template, publicUrl, clock.UtcNow), publicUrl);
            return new EmailPreviewResponse(content.Subject, content.Html, content.Text);
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/preview", (Request request, Handler handler) =>
            {
                var result = handler.Execute(request);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .Produces<EmailPreviewResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);
}
