using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class BulkMetadataJobConfiguration : IEntityTypeConfiguration<BulkMetadataJob>
{
    public void Configure(EntityTypeBuilder<BulkMetadataJob> entity)
    {
        entity.ToTable("bulk_metadata_job");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Status).HasMaxLength(30);
        entity.Property(x => x.LastError).HasMaxLength(500);
        // SQLite permits multiple NULL keys but only one active refresh across all admins.
        entity.HasIndex(x => x.ActiveKey).IsUnique();
    }
}
