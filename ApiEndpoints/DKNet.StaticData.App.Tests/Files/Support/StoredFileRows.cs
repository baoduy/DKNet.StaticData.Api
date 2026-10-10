using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// Moves a stored file's activity dates, for scenarios set months or on given days in the past. The table and column
/// names are read from the service's own EF Core model of <c>StoredFile</c>, so the test names no schema detail.
/// </summary>
internal static class StoredFileRows
{
    /// <summary>Sets the file's creation time to <paramref name="createdOn"/> and clears its update time.</summary>
    public static async Task SetCreatedOnAsync(FilesApi api, Guid fileId, DateTimeOffset createdOn)
    {
        using var scope = api.Host.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CoreDbContext>().Model;
        var entity = model.GetEntityTypes().Single(e => e.ClrType.Name == "StoredFile");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var s = api.Server;
        string Column(string property) => s.Quote(entity.FindProperty(property)!.GetColumnName(table)!);
        var name = table.Schema is null ? s.Quote(table.Name) : $"{s.Quote(table.Schema)}.{s.Quote(table.Name)}";

        var changed = await s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {name} SET {Column("CreatedOn")} = @createdOn, {Column("UpdatedOn")} = NULL WHERE {Column("Id")} = @id",
            ("createdOn", createdOn),
            ("id", fileId));
        changed.ShouldBe(1);
    }
}
