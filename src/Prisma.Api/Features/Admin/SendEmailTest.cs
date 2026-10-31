using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;

namespace Prisma.Api.Features.Admin;

// "Enviar teste para mim" (docs/admin-email.md, seção 4): o e-mail do modelo, com dados de exemplo, só para o endereço do próprio
// admin, com o assunto marcado. Sai na hora pelo transporte, sem passar pela fila nem contar como disparo (não grava nada), e
// vale também para modelo bloqueado: o dono confere como fica antes de o descadastro existir.
public static class SendEmailTest
{
    public const string SendFailed = "SendFailed";

    public sealed record Request(string? Template);

    public sealed class Handler(
        AppDbContext db, ICurrentUser currentUser, IEmailTransport transport, IOptions<EmailOptions> email, IOptions<AppOptions> app,
        IClock clock, ILogger<Handler> logger)
    {
        public async Task<Result<bool>> Execute(Request request, CancellationToken ct)
        {
            if (EmailCampaignCatalog.Find(request.Template) is not { } template)
                return EmailCampaignCatalog.UnknownTemplate;

            var userId = currentUser.UserId;
            var address = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(address))
                return new Error(ErrorType.Unauthorized, "Sessão expirada. Entre de novo.");

            var publicUrl = app.Value.PublicUrl;
            var content = EmailLayout.Render(CampaignEmails.Example(template, publicUrl, clock.UtcNow), publicUrl);
            var settings = email.Value;
            var result = await transport.SendAsync(
                new EmailMessage(settings.From, settings.ReplyTo, address, "[Teste] " + content.Subject, content.Html, content.Text), ct);

            if (result.Status != EmailSendStatus.Sent)
            {
                logger.EmailTestFailed(template.Kind, userId, result.Reason ?? "unknown");
                return new Error(ErrorType.Conflict, "O fornecedor de e-mail não aceitou o teste. Tente de novo em instantes.", SendFailed);
            }

            logger.EmailTestSent(template.Kind, userId);
            return true;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/test", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                if (result.IsSuccess)
                    return Results.NoContent();

                // Falha do fornecedor é 502, como a do Banco Central no admin do mercado.
                return result.Error.Code == SendFailed
                    ? Results.Problem(detail: result.Error.Message, statusCode: StatusCodes.Status502BadGateway,
                        title: "Fornecedor indisponível.", extensions: new Dictionary<string, object?> { ["code"] = SendFailed })
                    : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.AdminEmailRateLimitPolicy)
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(502);
}
