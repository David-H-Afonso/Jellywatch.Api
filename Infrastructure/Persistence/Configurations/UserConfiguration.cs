using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.ToTable("user");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.JellyfinUserId).HasColumnName("jellyfin_user_id");
        e.Property(x => x.Username).HasColumnName("username");
        e.Property(x => x.IsAdmin).HasColumnName("is_admin");
        e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
        e.Property(x => x.PreferredLanguage).HasColumnName("preferred_language");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(x => x.JellyfinUserId).IsUnique();
        e.HasIndex(x => x.Username).IsUnique();
    }
}
