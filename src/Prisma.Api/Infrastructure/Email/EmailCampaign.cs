namespace Prisma.Api.Infrastructure.Email;

// Um disparo do admin (docs/admin-email.md, seção 4): o histórico do que foi mandado, sem dado de ninguém. Tabela sem dono
// (AppDbContext.GlobalTables): quem pediu fica só no log, como nas sincronizações do mercado.
public sealed class EmailCampaign
{
    private EmailCampaign()
    {
    }

    public Guid Id { get; private set; }

    // Chave do pedido: repetir o mesmo pedido (toque duplo, tentativa de novo) devolve este disparo em vez de criar outro.
    public Guid ClientRequestId { get; private set; }

    public EmailKind Kind { get; private set; }

    // O "há N dias" do público, como o dono o escolheu.
    public int Days { get; private set; }

    public int Recipients { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static EmailCampaign Create(Guid clientRequestId, EmailKind kind, int days, int recipients, DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(),
        ClientRequestId = clientRequestId,
        Kind = kind,
        Days = days,
        Recipients = recipients,
        CreatedAt = utcNow,
    };
}

// Quem entrou num disparo. É o que impede mandar o mesmo modelo duas vezes à mesma pessoa (a fila apaga as linhas depois de
// 30 dias; este registro dura enquanto a conta). Sai com a conta (cascata a partir de users).
public sealed class EmailCampaignRecipient
{
    private EmailCampaignRecipient()
    {
    }

    public Guid CampaignId { get; private set; }

    public Guid UserId { get; private set; }

    // O tipo do disparo, repetido aqui para a consulta "já recebeu este modelo?" não precisar juntar tabelas.
    public EmailKind Kind { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static EmailCampaignRecipient Create(Guid campaignId, Guid userId, EmailKind kind, DateTime utcNow) => new()
    {
        CampaignId = campaignId,
        UserId = userId,
        Kind = kind,
        CreatedAt = utcNow,
    };
}
