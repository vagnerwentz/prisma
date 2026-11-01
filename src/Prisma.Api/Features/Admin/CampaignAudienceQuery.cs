using Microsoft.EntityFrameworkCore;
using Prisma.Api.Infrastructure;
using Prisma.Api.Infrastructure.Auth;
using Prisma.Domain;
using Prisma.Domain.Accounts;
using Prisma.Domain.Emails;

namespace Prisma.Api.Features.Admin;

// Quem está no público de um modelo agora (docs/admin-email.md, seção 4). É a mesma consulta para contar na tela e para escolher
// quem recebe no disparo: o número que o dono vê e a lista do envio não divergem. A regra é do domínio (CampaignAudience e
// CampaignCooldown); aqui ela vira SQL, comparando com o instante de corte da janela, e os testes de integração conferem as duas.
public sealed class CampaignAudienceQuery(AppDbContext db, IClock clock)
{
    public IQueryable<AppUser> Eligible(CampaignTemplate template, ActivityWindow window)
    {
        var activeSince = window.ActiveSince(clock.Today);
        var users = db.Users.AsNoTracking();

        users = template.Audience switch
        {
            CampaignAudienceKind.StuckSignup => users.Where(u => !u.EmailConfirmed && u.CreatedAt < activeSince),
            CampaignAudienceKind.Inactive => users.Where(u => u.EmailConfirmed && u.LastSeenAt != null && u.LastSeenAt < activeSince),
            _ => throw new ArgumentOutOfRangeException(nameof(template)),
        };

        // Quem já recebeu este modelo: sempre (uma vez por conta) ou dentro do intervalo mínimo entre disparos.
        var kind = template.Kind;
        var sentSince = CampaignCooldown.SentSince(clock.UtcNow, template.MinDaysBetween);
        return sentSince is null
            ? users.Where(u => !db.EmailCampaignRecipients.Any(r => r.UserId == u.Id && r.Kind == kind))
            : users.Where(u => !db.EmailCampaignRecipients.Any(r => r.UserId == u.Id && r.Kind == kind && r.CreatedAt > sentSince));
    }
}
