using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class PushNotificationDeliveryConfiguration : IEntityTypeConfiguration<PushNotificationDelivery>
{
    public void Configure(EntityTypeBuilder<PushNotificationDelivery> entity)
    {
        entity.ToTable("push_notification_delivery");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.UserId).HasColumnName("user_id");
        entity.Property(x => x.PushSubscriptionId).HasColumnName("push_subscription_id");
        entity.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(300).IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Body).HasColumnName("body").HasMaxLength(500).IsRequired();
        entity.Property(x => x.Url).HasColumnName("url").HasMaxLength(500).IsRequired();
        entity.Property(x => x.DueAtUtc).HasColumnName("due_at_utc");
        entity.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc");
        entity.Property(x => x.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(x => new { x.PushSubscriptionId, x.EventKey }).IsUnique();
        entity.HasIndex(x => new { x.Status, x.DueAtUtc, x.NextAttemptAtUtc });
        entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.PushSubscription).WithMany().HasForeignKey(x => x.PushSubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
