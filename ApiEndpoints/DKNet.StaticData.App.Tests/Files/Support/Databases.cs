using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// Spec DRK-2206 §3 "Tests": every scenario that touches the database runs on both Postgres and SQL Server, each in a
/// real container. Theory data for that.
/// </summary>
public static class Databases
{
    public static TheoryData<TestDatabase> Both => new() { TestDatabase.Postgres, TestDatabase.SqlServer };

    /// <summary>Every row of an outline's examples, once per database (the database first).</summary>
    public static IEnumerable<object[]> With(params object[][] rows) =>
        from database in new[] { TestDatabase.Postgres, TestDatabase.SqlServer }
        from row in rows
        select (object[])[database, .. row];
}
