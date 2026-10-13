using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.App.Tests.Files.Support;

/// <summary>
/// Moves a stored file's or a file group's activity dates, for scenarios set months or on given days in the past. The
/// table and column names are read from the service's own EF Core model of the entity, so the test names no schema
/// detail.
/// </summary>
internal static class StoredFileRows
{
    /// <summary>Sets the file's creation time to <paramref name="createdOn"/> and clears its update time.</summary>
    public static Task SetCreatedOnAsync(FilesApi api, Guid fileId, DateTimeOffset createdOn) =>
        SetActivityAsync(api, "StoredFile", fileId, createdOn, updatedOn: null);

    /// <summary>
    /// Sets the creation time of the <paramref name="entity"/> record <paramref name="id"/> (an EF Core entity name such as
    /// <c>StoredFile</c> or <c>FileGroup</c>) to <paramref name="createdOn"/> and its update time to
    /// <paramref name="updatedOn"/>, or clears it.
    /// </summary>
    public static async Task SetActivityAsync(
        FilesApi api,
        string entity,
        Guid id,
        DateTimeOffset createdOn,
        DateTimeOffset? updatedOn)
    {
        using var scope = api.Host.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CoreDbContext>().Model;
        var type = model.GetEntityTypes().SingleOrDefault(e => e.ClrType.Name == entity)
                   ?? throw new ShouldAssertException($"the service's model has no {entity} entity");
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        var s = api.Server;
        string Column(string property) => s.Quote(type.FindProperty(property)!.GetColumnName(table)!);
        var name = table.Schema is null ? s.Quote(table.Name) : $"{s.Quote(table.Schema)}.{s.Quote(table.Name)}";

        var changed = await s.ExecuteAsync(
            api.Host.ConnectionString,
            $"UPDATE {name} SET {Column("CreatedOn")} = @createdOn, {Column("UpdatedOn")} = " +
            (updatedOn is null ? "NULL" : "@updatedOn") + $" WHERE {Column("Id")} = @id",
            [("createdOn", createdOn), ("id", id), .. updatedOn is { } u ? new[] { ("updatedOn", (object)u) } : []]);
        changed.ShouldBe(1);
    }
}
