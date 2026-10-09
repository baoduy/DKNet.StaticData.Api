using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using DKNet.StaticData.Domains.Features.AutomatedSample.Entities;
using DKNet.StaticData.Domains.Features.ManualSample.Entities;
using DKNet.StaticData.Infra.Contexts;
using DKNet.StaticData.Infra.Migrations;

namespace DKNet.StaticData.App.Tests.Architecture;

public class MigrationSchemaTests
{
    [Fact]
    public void EfCoreModel_ShouldDeclareProductNameUnique()
    {
        // DRK-1410: the uniqueness rule is a check backed by a real guarantee — the database constraint,
        // not the validator, is what actually keeps a product name unique.
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var entityType = dbContext.Model.FindEntityType(typeof(Product));
        entityType.ShouldNotBeNull();

        var nameIndex = entityType!.GetIndexes()
            .SingleOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Product.Name));

        nameIndex.ShouldNotBeNull("Product.Name must have an index declared in the EF Core model.");
        nameIndex!.IsUnique.ShouldBeTrue("Product.Name's index must be unique.");
    }


    [Fact]
    public void EfCoreModel_ShouldDeclarePurchaseOrderOwnedBy_AsRequired_WithMaxLength500()
    {
        // DRK-1901: the column DataOwnerAuthQuery filters every purchase-order read on.
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var ownedBy = dbContext.Model.FindEntityType(typeof(PurchaseOrder))?.FindProperty(nameof(PurchaseOrder.OwnedBy));

        ownedBy.ShouldNotBeNull();
        ownedBy!.IsNullable.ShouldBeFalse();
        ownedBy.GetMaxLength().ShouldBe(500);
    }

    [Fact]
    public void AddPurchaseOrderOwnedByMigration_ShouldAddTheOwnedByColumn_AndDropItOnRollback()
    {
        var migration = new AddPurchaseOrderOwnedBy();

        var added = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<AddColumnOperation>();
        added.Name.ShouldBe("OwnedBy");
        added.Schema.ShouldBe("manual_sample");
        added.Table.ShouldBe("PurchaseOrders");
        added.ColumnType.ShouldBe("character varying(500)");
        added.IsNullable.ShouldBeFalse();

        var dropped = migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropColumnOperation>();
        dropped.Name.ShouldBe("OwnedBy");
        dropped.Schema.ShouldBe("manual_sample");
        dropped.Table.ShouldBe("PurchaseOrders");
    }

    [Fact]
    public void Migration_ShouldCreate_SeqSchema()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var sequences = dbContext.Model.GetSequences()
            .Select(s => s.Schema)
            .Distinct()
            .ToHashSet();
        sequences.ShouldContain("seq");
    }

    [Fact]
    public void Migration_ShouldHave_OnlySeqMembership_NotSeqNone()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var sequenceNames = dbContext.Model.GetSequences()
            .Select(s => s.Name)
            .ToArray();

        sequenceNames.ShouldContain("Seq_Membership");
        sequenceNames.Any(s => s.Contains("None", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
    }

    [Fact]
    public void EfCoreModel_ShouldTarget_PostgreSqlProvider()
    {
        using var dbContext = new DbContextFactory().CreateDbContext([]);
        var providerName = dbContext.Database.ProviderName;
        providerName.ShouldNotBeNullOrWhiteSpace();
        providerName!.Contains("SqlServer", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public void InfraCsproj_ShouldReference_NpgsqlNotSqlServer()
    {
        var srcDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                "../../../../../ApiEndpoints/DKNet.StaticData.Infra"));

        var csprojPath = Path.Combine(srcDir, "DKNet.StaticData.Infra.csproj");
        File.Exists(csprojPath).ShouldBeTrue();

        var content = File.ReadAllText(csprojPath);

        content.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal).ShouldBeTrue();
        content.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }
}