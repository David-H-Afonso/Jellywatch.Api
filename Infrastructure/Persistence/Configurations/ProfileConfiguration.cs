using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> e)
    {
        e.ToTable("profile");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.JellyfinUserId).HasColumnName("jellyfin_user_id");
        e.Property(x => x.DisplayName).HasColumnName("display_name");
        e.Property(x => x.IsJoint).HasColumnName("is_joint");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.User).WithMany(u => u.Profiles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.JellyfinUserId).IsUnique();
    }
}
