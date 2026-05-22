using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WatchlistAccessRequestConfiguration : IEntityTypeConfiguration<WatchlistAccessRequest>
{
    public void Configure(EntityTypeBuilder<WatchlistAccessRequest> e)
    {
        e.ToTable("watchlist_access_request");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WatchlistId).HasColumnName("watchlist_id");
        e.Property(x => x.RequestingUserId).HasColumnName("requesting_user_id");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Message).HasColumnName("message").HasMaxLength(1000);
        e.Property(x => x.RespondedByUserId).HasColumnName("responded_by_user_id");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.RespondedAt).HasColumnName("responded_at");
        e.HasOne(x => x.Watchlist).WithMany(w => w.AccessRequests).HasForeignKey(x => x.WatchlistId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.RequestingUser).WithMany().HasForeignKey(x => x.RequestingUserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.RespondedByUser).WithMany().HasForeignKey(x => x.RespondedByUserId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.WatchlistId, x.RequestingUserId, x.Status });
    }
}
