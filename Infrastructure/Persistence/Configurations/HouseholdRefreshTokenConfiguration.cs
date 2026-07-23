using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdRefreshTokenConfiguration : IEntityTypeConfiguration<HouseholdRefreshToken>
{
    public void Configure(EntityTypeBuilder<HouseholdRefreshToken> e)
    {
        e.ToTable("household_refresh_token");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasMaxLength(32);
        e.Property(x => x.ConnectionId).HasColumnName("connection_id").HasMaxLength(32);
        e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        e.Property(x => x.FamilyId).HasColumnName("family_id").HasMaxLength(32);
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        e.Property(x => x.ReplacedByTokenId).HasColumnName("replaced_by_token_id").HasMaxLength(32);
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.FamilyId);
        e.HasOne(x => x.Connection).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ReplacedByToken).WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.Restrict);
    }
}
