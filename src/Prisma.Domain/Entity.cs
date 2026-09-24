namespace Prisma.Domain;

// Base de toda entidade do usuário. CreatedAt, UpdatedAt e DeletedAt são preenchidos pela
// infraestrutura ao salvar (a partir do IClock); o domínio nunca os altera.
public abstract class Entity
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid UserId { get; protected init; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }
}
