using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WatchlistInvitationConfiguration : IEntityTypeConfiguration<WatchlistInvitation>
{
    public void Configure(EntityTypeBuilder<WatchlistInvitation> e)
    {
        e.ToTable("watchlist_invitation");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WatchlistId).HasColumnName("watchlist_id");
        e.Property(x => x.InvitedUserId).HasColumnName("invited_user_id");
        e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Role).HasColumnName("role");
        e.Property(x => x.CanAddItems).HasColumnName("can_add_items").HasDefaultValue(true);
        e.Property(x => x.CanRemoveItems).HasColumnName("can_remove_items").HasDefaultValue(false);
        e.Property(x => x.CanReorderItems).HasColumnName("can_reorder_items").HasDefaultValue(true);
        e.Property(x => x.CanUpdateItemStatus).HasColumnName("can_update_item_status").HasDefaultValue(true);
        e.Property(x => x.CanInviteMembers).HasColumnName("can_invite_members").HasDefaultValue(false);
        e.Property(x => x.CanManageMembers).HasColumnName("can_manage_members").HasDefaultValue(false);
        e.Property(x => x.CanUpdateWatchlist).HasColumnName("can_update_watchlist").HasDefaultValue(false);
        e.Property(x => x.Message).HasColumnName("message").HasMaxLength(1000);
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.RespondedAt).HasColumnName("responded_at");
        e.HasOne(x => x.Watchlist).WithMany(w => w.Invitations).HasForeignKey(x => x.WatchlistId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.InvitedUser).WithMany().HasForeignKey(x => x.InvitedUserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.InvitedByUser).WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.WatchlistId, x.InvitedUserId, x.Status });
    }
}
