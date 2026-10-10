using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.Infra.Features.Files.Mappers;

internal sealed class StoredFileConfigs : DefaultEntityTypeConfiguration<StoredFile>
{
    #region Methods

    public override void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        base.Configure(builder);

        // The id is made by StoredFile.Create (version 7), so the storage key can be built from it before the insert.
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.OwnedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(f => new { f.OwnedBy, f.CreatedOn });

        builder.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Checksum).HasMaxLength(64).IsRequired();

        builder.Property(f => f.StorageKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(f => f.StorageKey).IsUnique();

        builder.Property(f => f.Version).IsConcurrencyToken();

        builder.ToTable("StoredFiles", DomainSchemas.Files);
    }

    #endregion
}
