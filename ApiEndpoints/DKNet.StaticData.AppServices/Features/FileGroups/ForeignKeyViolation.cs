using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>Tells a save the database refused for a foreign key from any other failed save, on both databases.</summary>
internal static class ForeignKeyViolation
{
    #region Fields

    private const string PostgresSqlState = "23503";
    private const int SqlServerNumber = 547;

    #endregion

    #region Methods

    /// <summary>
    ///     Whether <paramref name="exception" /> is Postgres SQLSTATE 23503 or SQL Server error 547. SQL Server's
    ///     <c>SqlException</c> carries its error number only in its own <c>Number</c> property, which is read by name:
    ///     this layer references no database driver.
    /// </summary>
    // ponytail: reflection on SqlException.Number; move to a typed check if a layer ever references Microsoft.Data.SqlClient.
    public static bool IsIn(DbUpdateException exception) =>
        exception.InnerException is DbException inner
        && (inner.SqlState == PostgresSqlState
            || (inner.GetType().FullName == "Microsoft.Data.SqlClient.SqlException"
                && inner.GetType().GetProperty("Number")?.GetValue(inner) is SqlServerNumber));

    #endregion
}
