using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class MetadataDailyCycleConfiguration : IEntityTypeConfiguration<MetadataDailyCycle>
{
    public void Configure(EntityTypeBuilder<MetadataDailyCycle> entity)
    {
        entity.ToTable("metadata_daily_cycle");
        entity.HasKey(cycle => cycle.LocalDate);
        entity.Property(cycle => cycle.LocalDate).HasMaxLength(10);
        entity.HasOne(cycle => cycle.Job).WithMany().HasForeignKey(cycle => cycle.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
