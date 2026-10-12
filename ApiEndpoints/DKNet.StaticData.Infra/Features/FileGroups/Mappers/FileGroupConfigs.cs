using DKNet.StaticData.Domains.Features.FileGroups.Entities;

namespace DKNet.StaticData.Infra.Features.FileGroups.Mappers;

internal sealed class FileGroupConfigs : DefaultEntityTypeConfiguration<FileGroup>
{
    #region Methods

    public override void Configure(EntityTypeBuilder<FileGroup> builder)
    {
        base.Configure(builder);

        // The id is made by FileGroup.Create (version 7).
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.OwnedBy).HasMaxLength(255).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Purpose).HasMaxLength(100);
        builder.Property(g => g.ExternalRef).HasMaxLength(255);

        builder.HasIndex(g => new { g.OwnedBy, g.CreatedOn });
        builder.HasIndex(g => new { g.OwnedBy, g.Purpose });
        builder.HasIndex(g => new { g.OwnedBy, g.ExternalRef });

        builder.Property(g => g.Version).IsConcurrencyToken();

        builder.ToTable("FileGroups", DomainSchemas.Files);
    }

    #endregion
}
