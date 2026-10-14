namespace Prisma.Api.Infrastructure.Privacy;

// Uma conta apagada pela própria pessoa (etapa A.3). Só o id e o instante, por 90 dias (o prazo das cópias de
// segurança): restaurado um backup, a conta volta, e a tarefa de limpeza a apaga de novo (docs/operacao.md, 5.6).
// Tabela sem dono (AppDbContext.GlobalTables): o usuário a que o id se refere não existe mais.
public sealed class DeletedAccount
{
    private DeletedAccount()
    {
    }

    public Guid UserId { get; private set; }

    public DateTime DeletedAt { get; private set; }
}
