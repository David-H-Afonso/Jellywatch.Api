using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdAuthorizationCodeConfiguration : IEntityTypeConfiguration<HouseholdAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<HouseholdAuthorizationCode> e)
    {
        e.ToTable("household_authorization_code");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasMaxLength(32);
        e.Property(x => x.ConnectionId).HasColumnName("connection_id").HasMaxLength(32);
        e.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        e.Property(x => x.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2048);
        e.Property(x => x.CodeChallenge).HasColumnName("code_challenge").HasMaxLength(128);
        e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        e.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        e.HasIndex(x => x.CodeHash).IsUnique();
        e.HasOne(x => x.Connection).WithMany(x => x.AuthorizationCodes).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
