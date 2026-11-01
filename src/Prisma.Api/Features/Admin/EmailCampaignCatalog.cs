using Prisma.Api.Infrastructure.Email;
using Prisma.Domain;
using Prisma.Domain.Emails;

namespace Prisma.Api.Features.Admin;

public enum CampaignAudienceKind
{
    // Cadastrou e não confirmou o e-mail há N dias ou mais.
    StuckSignup,

    // Confirmou o e-mail e está sem acesso há N dias ou mais.
    Inactive,
}

// Um modelo de disparo: o e-mail, a quem vai e com que cuidado (docs/admin-email.md, seção 4). Modelo novo entra aqui, com o
// montador do e-mail (IEmailComposer) e o texto (CampaignEmails).
public sealed record CampaignTemplate(
    string Code,
    EmailKind Kind,
    CampaignCategory Category,
    CampaignAudienceKind Audience,
    string Name,
    string Description,
    string AudienceLabel,
    // O "há N dias" que a tela propõe; o dono troca.
    int DefaultDays,
    // Quanto esperar para mandar o mesmo modelo à mesma pessoa de novo; nulo = uma vez por conta, para sempre.
    int? MinDaysBetween);

public static class EmailCampaignCatalog
{
    // Enquanto não há o descadastro (etapa seguinte), um modelo de relacionamento não sai para ninguém: a trava é de código, não de
    // cuidado. Vira verdadeiro junto com o link para a pessoa deixar de receber.
    public static bool UnsubscribeAvailable => false;

    public static readonly Error UnknownTemplate = new(ErrorType.NotFound, "Modelo de e-mail desconhecido.", "UnknownTemplate");

    public static readonly IReadOnlyList<CampaignTemplate> All =
    [
        new("confirm-reminder", EmailKind.ConfirmEmailReminder, CampaignCategory.Account, CampaignAudienceKind.StuckSignup,
            "Falta confirmar o e-mail",
            "Lembra quem criou a conta e não confirmou o e-mail, com um link novo. Um lembrete por conta, para sempre.",
            "Contas sem o e-mail confirmado há N dias ou mais",
            DefaultDays: 3, MinDaysBetween: null),
        new("miss-you", EmailKind.MissYou, CampaignCategory.Relationship, CampaignAudienceKind.Inactive,
            "Sentimos sua falta",
            "Convida a voltar quem confirmou o e-mail e está sem acessar o app. Só sai com o descadastro.",
            "Contas sem acesso há N dias ou mais",
            DefaultDays: 30, MinDaysBetween: 30),
    ];

    public static CampaignTemplate? Find(string? code) =>
        All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

    // Por que o modelo não pode sair para o público, ou nulo se pode. Vale também para a tela.
    public static string? BlockedCode(CampaignTemplate template) =>
        template.Category == CampaignCategory.Relationship && !UnsubscribeAvailable ? "UnsubscribeRequired" : null;
}
