using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> entity)
    {
        entity.ToTable("push_subscription");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.UserId).HasColumnName("user_id");
        entity.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(2048).IsRequired();
        entity.Property(x => x.P256dh).HasColumnName("p256dh").HasMaxLength(512).IsRequired();
        entity.Property(x => x.Auth).HasColumnName("auth").HasMaxLength(512).IsRequired();
        entity.Property(x => x.DeviceName).HasColumnName("device_name").HasMaxLength(200);
        entity.Property(x => x.IsActive).HasColumnName("is_active");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.Endpoint).IsUnique();
        entity.HasIndex(x => new { x.UserId, x.IsActive });
        entity.HasOne(x => x.User).WithMany(x => x.PushSubscriptions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
