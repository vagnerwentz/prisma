using Prisma.Domain;

namespace Prisma.Api.Tests.Infrastructure;

public sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
