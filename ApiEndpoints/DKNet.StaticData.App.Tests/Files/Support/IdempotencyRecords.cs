namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// Reads and seeds the idempotency records the service keeps in its own database, in the <c>IdempotencyKeys</c> table
/// the DKNet idempotency store 13.2.5 creates (its <c>Initial</c> migration, the same columns on both databases). The
/// store stamps its records with the wall clock, so a scenario set "N seconds after the first request" moves the
/// record's times back by N seconds rather than waiting.
/// </summary>
internal static class IdempotencyRecords
{
    private const string Table = "IdempotencyKeys";

    /// <summary>Adds a finished record for <paramref name="key"/> that expires at <paramref name="expiresAt"/>.</summary>
    public static Task InsertAsync(FilesApi api, string key, DateTimeOffset expiresAt)
    {
        var s = api.Server;
        var columns = string.Join(", ", new[]
        {
            "Id", "IdempotentKey", "Endpoint", "Method", "CompositeKey", "StatusCode", "Body", "ContentType",
            "CreatedAt", "ExpiresAt"
        }.Select(s.Quote));
        return s.ExecuteAsync(
            api.Host.ConnectionString,
            $"INSERT INTO {s.Quote(Table)} ({columns}) " +
            "VALUES (@id, @key, @endpoint, @method, @composite, 201, @body, @contentType, @createdAt, @expiresAt)",
            ("id", Guid.NewGuid()),
            ("key", key),
            ("endpoint", FilesApi.Route),
            ("method", "POST"),
            ("composite", $"seeded:{key}"),
            ("body", "{}"),
            ("contentType", "application/json"),
            ("createdAt", expiresAt.AddHours(-4)),
            ("expiresAt", expiresAt));
    }

    /// <summary>The idempotency key of every record the store holds.</summary>
    public static Task<IReadOnlyList<string>> KeysAsync(FilesApi api) =>
        api.Server.QueryTextsAsync(
            api.Host.ConnectionString,
            $"SELECT {api.Server.Quote("IdempotentKey")} FROM {api.Server.Quote(Table)}");

    /// <summary>Moves every record's creation and expiry back by <paramref name="seconds"/>.</summary>
    public static Task AgeAsync(FilesApi api, int seconds)
    {
        var s = api.Server;
        var (created, expires) = (s.Quote("CreatedAt"), s.Quote("ExpiresAt"));
        var sql = api.Host.Database == TestSupport.TestDatabase.Postgres
            ? $"UPDATE {s.Quote(Table)} SET {created} = {created} - @seconds * interval '1 second', " +
              $"{expires} = {expires} - @seconds * interval '1 second'"
            : $"UPDATE {s.Quote(Table)} SET {created} = DATEADD(second, -@seconds, {created}), " +
              $"{expires} = DATEADD(second, -@seconds, {expires})";
        return s.ExecuteAsync(api.Host.ConnectionString, sql, ("seconds", seconds));
    }
}
