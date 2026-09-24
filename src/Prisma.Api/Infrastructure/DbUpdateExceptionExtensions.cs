using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Prisma.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;
}
