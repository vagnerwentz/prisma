using Prisma.Domain;

namespace Prisma.Api.Infrastructure;

// Único ponto da aplicação que lê o relógio do sistema. Verificado por teste de arquitetura.
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
