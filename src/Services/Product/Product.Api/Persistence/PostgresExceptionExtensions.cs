using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Product.Api.Persistence;

internal static class PostgresExceptionExtensions
{
    internal static bool IsUniqueConstraintViolation(
        this DbUpdateException exception,
        string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var actualConstraint
        } && string.Equals(actualConstraint, constraintName, StringComparison.Ordinal);
}
