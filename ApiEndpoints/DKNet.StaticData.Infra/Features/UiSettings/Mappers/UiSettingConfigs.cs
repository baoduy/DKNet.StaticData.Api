using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;

namespace DKNet.StaticData.Infra.Features.UiSettings.Mappers;

internal sealed class UiSettingConfigs : DefaultEntityTypeConfiguration<UiSetting>
{
    #region Methods

    public override void Configure(EntityTypeBuilder<UiSetting> builder)
    {
        base.Configure(builder);

        // The id is made by UiSetting.Create (version 7).
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.OwnedBy).HasMaxLength(255).IsRequired();
        builder.Property(s => s.SettingKey).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SettingType).HasMaxLength(50).IsRequired();

        // At most 65,536 bytes as sent, so never more characters: nvarchar(max) on SQL Server, text on Postgres
        // (CoreDbContext), so the value keeps every byte as sent.
        builder.Property(s => s.Value).HasMaxLength(65_536).IsRequired();

        // At most one setting per owner and key.
        builder.HasIndex(s => new { s.OwnedBy, s.SettingKey }).IsUnique();
        builder.HasIndex(s => new { s.OwnedBy, s.CreatedOn });

        builder.Property(s => s.Version).IsConcurrencyToken();

        // A group with a linked setting is never deleted: the database refuses it, never cascades. EF Core indexes the
        // foreign key (IX_UiSettings_GroupId) by convention.
        builder.HasOne<SettingGroup>().WithMany().HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("UiSettings", DomainSchemas.Settings);
    }

    #endregion
}
