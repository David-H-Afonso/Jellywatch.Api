using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdAccessTokenConfiguration : IEntityTypeConfiguration<HouseholdAccessToken>
{
    public void Configure(EntityTypeBuilder<HouseholdAccessToken> e)
    {
        e.ToTable("household_access_token");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasMaxLength(32);
        e.Property(x => x.ConnectionId).HasColumnName("connection_id").HasMaxLength(32);
        e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasOne(x => x.Connection).WithMany(x => x.AccessTokens).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
