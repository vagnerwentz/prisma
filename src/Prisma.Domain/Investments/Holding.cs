namespace Prisma.Domain.Investments;

// Um ativo da carteira da pessoa: "eu tenho BBAS3" (docs/investimentos.md, seção 8, etapa 5a). Aponta para o
// catálogo global de ativos (Asset), que é de todos; o Holding é só dela. Sem quantidade nem preço médio por
// enquanto: chegam com as operações (compra e venda).
//
// Um por ativo por pessoa. Tirar da carteira é soft delete; pôr de novo devolve o mesmo registro (o
// "Desfazer"), e o que um dia apontar para ele continua apontando.
public sealed class Holding : Entity
{
    private Holding() { }

    public Guid AssetId { get; private init; }

    public static Holding Add(Guid userId, Guid assetId) => new() { UserId = userId, AssetId = assetId };

    public void Restore() => ClearDeletion();
}
