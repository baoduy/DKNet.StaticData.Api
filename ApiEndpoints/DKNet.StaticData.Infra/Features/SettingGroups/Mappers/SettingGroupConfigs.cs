using DKNet.StaticData.Domains.Features.SettingGroups.Entities;

namespace DKNet.StaticData.Infra.Features.SettingGroups.Mappers;

internal sealed class SettingGroupConfigs : DefaultEntityTypeConfiguration<SettingGroup>
{
    #region Methods

    public override void Configure(EntityTypeBuilder<SettingGroup> builder)
    {
        base.Configure(builder);

        // The id is made by SettingGroup.Create (version 7).
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.OwnedBy).HasMaxLength(255).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(500);

        // At most one group per owner and name.
        builder.HasIndex(g => new { g.OwnedBy, g.Name }).IsUnique();
        builder.HasIndex(g => new { g.OwnedBy, g.CreatedOn });

        builder.Property(g => g.Version).IsConcurrencyToken();

        builder.ToTable("SettingGroups", DomainSchemas.Settings);
    }

    #endregion
}
