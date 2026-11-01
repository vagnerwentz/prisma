using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Api.Infrastructure.Email;
using Prisma.Api.Infrastructure.Http;
using Prisma.Api.Infrastructure.Logging;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Emails;

namespace Prisma.Api.Features.Admin;

// Um disparo do admin (docs/admin-email.md, seção 4): um modelo para o público dele, depois de o dono ver a prévia, mandar o teste e
// confirmar a contagem. Põe um e-mail por pessoa na fila (a tarefa de sempre os envia, com o conteúdo e o token montados na hora),
// escalonados, para que o e-mail de senha de outra pessoa nunca espere um disparo inteiro e a cota do fornecedor aguente.
//
// Tudo numa transação só: o disparo, quem entrou nele e os e-mails da fila, ou nada. Dois disparos do mesmo modelo ao mesmo tempo
// não se cruzam (lock do Postgres por modelo), e o mesmo pedido repetido devolve o disparo que já existe, sem enfileirar de novo.
public static class SendEmailCampaign
{
    public sealed record Request(string? Template, int Days, int ExpectedCount, Guid ClientRequestId);

    // A chave do lock de um modelo: pública para o teste segurar o mesmo lock e provar que o envio espera a vez.
    public static string LockKey(EmailKind kind) => $"email-campaign:{kind}";

    public static readonly Error InvalidRequest = new(ErrorType.Validation, "Pedido inválido.", "InvalidRequest");

    public sealed class Handler(
        AppDbContext db, CampaignAudienceQuery audience, ICurrentUser currentUser, IOptions<EmailOptions> options, IClock clock,
        ILogger<Handler> logger)
    {
        public async Task<Result<EmailCampaignResponse>> Execute(Request request, CancellationToken ct)
        {
            if (EmailCampaignCatalog.Find(request.Template) is not { } template)
                return EmailCampaignCatalog.UnknownTemplate;
            if (request.ClientRequestId == Guid.Empty || request.ExpectedCount < 0)
                return InvalidRequest;
            var window = ActivityWindow.Of(request.Days);
            if (!window.IsSuccess)
                return window.Error;

            var userId = currentUser.UserId;

            // O toque duplo ou a nova tentativa de um pedido que já passou: devolve o disparo, sem mandar nada de novo.
            if (await Existing(request.ClientRequestId, template, ct) is { } replay)
                return replay;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Um disparo por vez de cada modelo: o segundo espera o primeiro gravar quem recebeu e, ao olhar o público, já não os vê.
            var lockKey = LockKey(template.Kind);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

            if (await Existing(request.ClientRequestId, template, ct) is { } replayAfterLock)
                return replayAfterLock;

            var settings = options.Value.Campaigns;
            var eligible = await audience.Eligible(template, window.Value).CountAsync(ct);
            var guard = CampaignGuard.Check(
                template.Category, EmailCampaignCatalog.UnsubscribeAvailable, eligible, request.ExpectedCount, settings.MaxRecipients);
            if (!guard.IsSuccess)
            {
                logger.EmailCampaignRefused(template.Kind, userId, guard.Error.Code ?? "unknown");
                return guard.Error;
            }

            var people = await audience.Eligible(template, window.Value)
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Select(u => new { u.Id, Address = u.Email! })
                .Take(settings.MaxRecipients)
                .ToListAsync(ct);

            var now = clock.UtcNow;
            var campaign = EmailCampaign.Create(request.ClientRequestId, template.Kind, window.Value.Days, people.Count, now);
            db.EmailCampaigns.Add(campaign);
            for (var i = 0; i < people.Count; i++)
            {
                db.EmailCampaignRecipients.Add(EmailCampaignRecipient.Create(campaign.Id, people[i].Id, template.Kind, now));
                db.OutboxEmails.Add(OutboxEmail.Create(
                    template.Kind, people[i].Id, people[i].Address, now, firstAttemptAt: now.AddSeconds(i * settings.StaggerSeconds)));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.EmailCampaignSent(campaign.Id, template.Kind, userId, campaign.Recipients, campaign.Days);
            return ListEmailCampaigns.Respond(campaign);
        }

        private async Task<Result<EmailCampaignResponse>?> Existing(Guid clientRequestId, CampaignTemplate template, CancellationToken ct)
        {
            var found = await db.EmailCampaigns.AsNoTracking().SingleOrDefaultAsync(c => c.ClientRequestId == clientRequestId, ct);
            if (found is null)
                return null;

            // A mesma chave para outro modelo é erro de quem chama, não uma repetição.
            return found.Kind == template.Kind
                ? ListEmailCampaigns.Respond(found)
                : new Error(ErrorType.Conflict, "Este pedido já foi usado para outro e-mail.", "RequestIdReused");
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/send", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Execute(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(AuthSetup.AdminEmailRateLimitPolicy)
            .Produces<EmailCampaignResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(429);
}
