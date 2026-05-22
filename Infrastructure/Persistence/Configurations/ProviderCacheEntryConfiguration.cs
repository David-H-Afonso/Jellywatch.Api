using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ProviderCacheEntryConfiguration : IEntityTypeConfiguration<ProviderCacheEntry>
{
    public void Configure(EntityTypeBuilder<ProviderCacheEntry> e)
    {
        e.ToTable("provider_cache_entry");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.Provider).HasColumnName("provider");
        e.Property(x => x.ExternalId).HasColumnName("external_id");
        e.Property(x => x.ResponseJson).HasColumnName("response_json");
        e.Property(x => x.CachedAt).HasColumnName("cached_at");
        e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        e.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
        e.HasIndex(x => x.ExpiresAt);
    }
}
