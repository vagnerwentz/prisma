namespace Prisma.Domain.Emails;

/// <summary>
/// Um e-mail de conta (confirmação) é parte do serviço contratado; um de relacionamento ("sentimos sua falta") é opcional
/// para a pessoa e só sai com o descadastro à mão dela (docs/admin-email.md, seção 4).
/// </summary>
public enum CampaignCategory
{
    Account,
    Relationship,
}

/// <summary>
/// As travas de um disparo, na ordem em que o dono as encontra: o modelo pode sair?, há alguém?, cabe no teto?, a contagem que
/// ele viu ainda vale? Devolve o número de pessoas que vão receber.
/// </summary>
public static class CampaignGuard
{
    public static Result<int> Check(CampaignCategory category, bool unsubscribeAvailable, int eligible, int expected, int cap)
    {
        if (category == CampaignCategory.Relationship && !unsubscribeAvailable)
            return new Error(ErrorType.Validation,
                "Este e-mail ainda não pode ser enviado: falta o link para a pessoa deixar de receber.", "UnsubscribeRequired");

        if (eligible == 0)
            return new Error(ErrorType.Validation, "Ninguém no público escolhido.", "NoRecipients");

        if (eligible > cap)
            return new Error(ErrorType.Validation,
                $"O público tem {eligible} pessoas e o limite por disparo é {cap}.", "TooManyRecipients");

        // A contagem muda entre olhar e enviar (alguém confirmou, outro disparo saiu): o envio recusa em vez de mandar a mais.
        if (eligible != expected)
            return new Error(ErrorType.Conflict,
                $"O público mudou: agora são {eligible} pessoas. Confira e envie de novo.", "AudienceChanged");

        return eligible;
    }
}
