using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class UserWatchlistPreferenceConfiguration : IEntityTypeConfiguration<UserWatchlistPreference>
{
    public void Configure(EntityTypeBuilder<UserWatchlistPreference> e)
    {
        e.ToTable("user_watchlist_preference");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.DefaultWatchlistId).HasColumnName("default_watchlist_id");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.User).WithOne(u => u.WatchlistPreference).HasForeignKey<UserWatchlistPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.DefaultWatchlist).WithMany().HasForeignKey(x => x.DefaultWatchlistId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.UserId).IsUnique();
    }
}
