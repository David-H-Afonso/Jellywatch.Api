using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class PropagationRuleConfiguration : IEntityTypeConfiguration<PropagationRule>
{
    public void Configure(EntityTypeBuilder<PropagationRule> e)
    {
        e.ToTable("propagation_rule");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.SourceProfileId).HasColumnName("source_profile_id");
        e.Property(x => x.TargetProfileId).HasColumnName("target_profile_id");
        e.Property(x => x.IsActive).HasColumnName("is_active");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.HasOne(x => x.SourceProfile).WithMany(p => p.SourceRules).HasForeignKey(x => x.SourceProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.TargetProfile).WithMany(p => p.TargetRules).HasForeignKey(x => x.TargetProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.SourceProfileId, x.TargetProfileId }).IsUnique();
    }
}
