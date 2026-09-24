namespace Prisma.Domain;

public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today => SaoPauloTime.DateOf(UtcNow);
}
