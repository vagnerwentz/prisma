using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Prisma.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;

    // O Postgres abortou esta transação para desfazer um impasse com outra; refazer resolve.
    public static bool IsDeadlock(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected };
}
