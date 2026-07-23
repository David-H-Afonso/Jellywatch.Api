using Jellywatch.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public sealed class HouseholdConnectionConfiguration : IEntityTypeConfiguration<HouseholdConnection>
{
    public void Configure(EntityTypeBuilder<HouseholdConnection> e)
    {
        e.ToTable("household_connection");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").HasMaxLength(32);
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.ProfileId).HasColumnName("profile_id");
        e.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(100);
        e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(64);
        e.Property(x => x.GrantedScopes).HasColumnName("granted_scopes").HasMaxLength(512);
        e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        e.HasIndex(x => x.AccountId).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ProfileId, x.ClientId });
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}
