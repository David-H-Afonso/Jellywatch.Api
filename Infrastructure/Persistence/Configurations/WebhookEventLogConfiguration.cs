using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WebhookEventLogConfiguration : IEntityTypeConfiguration<WebhookEventLog>
{
    public void Configure(EntityTypeBuilder<WebhookEventLog> e)
    {
        e.ToTable("webhook_event_log");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.RawPayload).HasColumnName("raw_payload");
        e.Property(x => x.EventType).HasColumnName("event_type");
        e.Property(x => x.ReceivedAt).HasColumnName("received_at");
        e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        e.Property(x => x.Success).HasColumnName("success");
        e.Property(x => x.ErrorMessage).HasColumnName("error_message");
    }
}
