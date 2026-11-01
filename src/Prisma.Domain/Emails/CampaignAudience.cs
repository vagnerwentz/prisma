using Prisma.Domain.Accounts;

namespace Prisma.Domain.Emails;

/// <summary>
/// Quem entra no público de um disparo do admin (docs/admin-email.md, seção 4). A contagem é a da tela de Pessoas
/// (<see cref="ActivityWindow"/>): dias civis de São Paulo entre o fato e hoje, e "há N dias ou mais" é o que a janela não
/// considera mais recente. Assim o número que o dono vê e a lista que o disparo usa nunca divergem. O SQL do disparo compara
/// com <see cref="ActivityWindow.ActiveSince"/>; estas funções são a regra, e os testes de integração conferem que o SQL a segue.
/// </summary>
public static class CampaignAudience
{
    /// <summary>Cadastrou e não confirmou o e-mail há N dias ou mais.</summary>
    public static bool IsStuckSignup(bool emailConfirmed, DateTime createdAtUtc, ActivityWindow window, DateOnly today) =>
        !emailConfirmed && !window.IsActive(createdAtUtc, today);

    /// <summary>
    /// Confirmou o e-mail e o último acesso foi há N dias ou mais. Sem acesso gravado não é "sem acesso há N dias" (é "nunca
    /// usou", outra mensagem, e o carimbo da migration não prova nada): fica fora.
    /// </summary>
    public static bool IsInactive(bool emailConfirmed, DateTime? lastSeenAtUtc, ActivityWindow window, DateOnly today) =>
        emailConfirmed && lastSeenAtUtc is { } seen && !window.IsActive(seen, today);
}

/// <summary>
/// Quanto esperar antes de mandar o mesmo modelo à mesma pessoa de novo. <c>minDays</c> nulo = uma vez por conta, para sempre.
/// </summary>
public static class CampaignCooldown
{
    public static bool IsEligible(DateTime? lastSentUtc, DateTime nowUtc, int? minDays) =>
        lastSentUtc is null || (minDays is { } days && lastSentUtc.Value <= nowUtc.AddDays(-days));

    /// <summary>
    /// O instante a partir do qual um envio anterior ainda conta: quem recebeu antes ou nele já pode receber de novo. Nulo quando
    /// o modelo é de uma vez só (qualquer envio anterior conta). É a forma que o SQL consegue comparar.
    /// </summary>
    public static DateTime? SentSince(DateTime nowUtc, int? minDays) => minDays is { } days ? nowUtc.AddDays(-days) : null;
}
