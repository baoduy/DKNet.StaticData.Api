using DKNet.EfCore.Abstractions.Entities;
using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DKNet.StaticData.Infra.Contexts;

internal class CoreDbContext(DbContextOptions options, IEnumerable<IDataOwnerProvider>? dataKeyProviders = null)
    : DbContext(options), IDataOwnerDbContext
{
    #region Properties

    public IEnumerable<string> AccessibleKeys =>
        _dataKeyProvider is not null ? _dataKeyProvider.GetAccessibleKeys() : [];

    #endregion

    private const string ExactCollation = "Latin1_General_100_BIN2";

    private readonly IDataOwnerProvider? _dataKeyProvider = dataKeyProviders?.FirstOrDefault();

    #region Methods

    /// <summary>
    /// EF Core's own Error entry for a failed save is not written: it quotes the database's message, and SQL Server's
    /// duplicate key message holds the key, the owner included (spec DRK-2245, no owner in any log entry). A save that
    /// loses a race for a unique key answers 409 without it; any other failed save still writes the service's own
    /// Error entry, naming the database and the exception type.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.SaveChangesFailed));

    /// <summary>
    /// On SQL Server, every owner column, the file group's purpose and external reference, the setting key and the
    /// setting group name compare exactly, as they do on Postgres by default (design <c>04-data.md</c>, ADR-0002): the
    /// binary collation <see cref="ExactCollation"/>. Runs after DKNet's auto-config has applied every entity
    /// configuration, so each <see cref="IOwnedBy"/> entity is already in the model.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        if (!this.IsSqlServer()) return;

        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => typeof(IOwnedBy).IsAssignableFrom(e.ClrType)))
        {
            entity.GetProperty(nameof(IOwnedBy.OwnedBy)).SetCollation(ExactCollation);
        }

        var group = modelBuilder.Entity<FileGroup>();
        group.Property(g => g.Purpose).UseCollation(ExactCollation);
        group.Property(g => g.ExternalRef).UseCollation(ExactCollation);
        modelBuilder.Entity<UiSetting>().Property(s => s.SettingKey).UseCollation(ExactCollation);
        modelBuilder.Entity<SettingGroup>().Property(g => g.Name).UseCollation(ExactCollation);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureOwnershipResolvable();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnershipResolvable();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureOwnershipResolvable();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fails closed, before EF Core attempts the insert, when an authenticated caller's ownership key cannot be
    /// resolved and a new row would be left with no <see cref="IAuditedProperties.CreatedBy"/>, or a new
    /// <see cref="IOwnedBy"/> row with no <see cref="IOwnedBy.OwnedBy"/> — otherwise EF Core's own
    /// required-property check throws a raw <see cref="DbUpdateException"/> that leaks column/entity names into
    /// the response (DRK-899 R3: null key means "no stamp", never a crash), or the row is saved but readable by
    /// nobody (DRK-1901). A row that already carries its own <see cref="IOwnedBy.OwnedBy"/>, such as static seed
    /// data, is kept as it is.
    /// </summary>
    private void EnsureOwnershipResolvable()
    {
        if (_dataKeyProvider is null) return;
        if (!string.IsNullOrEmpty(_dataKeyProvider.GetOwnershipKey())) return;

        var hasUnattributableInsert = ChangeTracker.Entries()
            .Any(e => e.State == EntityState.Added
                      && (e.Entity is IOwnedBy { OwnedBy: null or "" }
                          || (e.Entity is IAuditedProperties { CreatedBy: null or "" }
                              && e.Metadata.FindProperty(nameof(IAuditedProperties.CreatedBy)) is { IsNullable: false })));

        if (hasUnattributableInsert) throw new OwnershipRequiredException();
    }

    #endregion
}
