using System.Collections.Concurrent;

namespace Prisma.Api.Infrastructure.Auth;

// Memória do "já carimbei esta pessoa neste dia", só para o LastSeenStamper não ir ao banco em todo pedido. É
// otimização, nunca a fonte da verdade: quem decide é o UPDATE condicional no Postgres, que é idempotente. Perder a
// memória (deploy, reinício, mais de uma instância) custa um UPDATE a mais por pessoa e por dia, nunca um valor errado.
// Interface com ValueTask porque uma implementação compartilhada (Redis) seria assíncrona; trocar é registrar outra
// classe, sem mexer no carimbador. Hoje há uma instância só, então a memória do processo basta (docs/admin-email.md, 2).
public interface ILastSeenCache
{
    ValueTask<bool> WasStampedAsync(Guid userId, DateOnly day);

    ValueTask RememberAsync(Guid userId, DateOnly day);
}

// Uma entrada por pessoa (o dia mais recente carimbado): cresce com o número de contas, não com o de pedidos.
public sealed class InMemoryLastSeenCache : ILastSeenCache
{
    private readonly ConcurrentDictionary<Guid, DateOnly> _stampedOn = new();

    public ValueTask<bool> WasStampedAsync(Guid userId, DateOnly day) =>
        ValueTask.FromResult(_stampedOn.TryGetValue(userId, out var stamped) && stamped == day);

    public ValueTask RememberAsync(Guid userId, DateOnly day)
    {
        _stampedOn[userId] = day;
        return ValueTask.CompletedTask;
    }
}
