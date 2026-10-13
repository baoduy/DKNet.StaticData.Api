using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>Tells a save the database refused for a duplicate unique key from any other failed save, on both databases.</summary>
internal static class UniqueViolation
{
    #region Fields

    private const string PostgresSqlState = "23505";
    private const int SqlServerUniqueIndexNumber = 2601;
    private const int SqlServerUniqueConstraintNumber = 2627;

    #endregion

    #region Methods

    /// <summary>
    ///     Whether <paramref name="exception" /> is Postgres SQLSTATE 23505 or SQL Server error 2601 (unique index) or 2627
    ///     (unique constraint or primary key). The SQL Server error number is read by name, as
    ///     <see cref="ForeignKeyViolation" /> reads it: this layer references no database driver.
    /// </summary>
    // ponytail: reflection on SqlException.Number; move to a typed check if a layer ever references Microsoft.Data.SqlClient.
    public static bool IsIn(DbUpdateException exception) =>
        exception.InnerException is DbException inner
        && (inner.SqlState == PostgresSqlState
            || (inner.GetType().FullName == "Microsoft.Data.SqlClient.SqlException"
                && inner.GetType().GetProperty("Number")?.GetValue(inner) is SqlServerUniqueIndexNumber
                    or SqlServerUniqueConstraintNumber));

    #endregion
}
