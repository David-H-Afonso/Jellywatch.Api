using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Common;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Infrastructure.ExternalServices;

namespace Jellywatch.Api.Application.Services;

public class AdminService : IAdminService
{
    private readonly JellywatchDbContext _context;
    private readonly IMetadataResolutionService _metadataService;
    private readonly IJellyfinApiClient _jellyfinClient;
    private readonly JellyfinSettings _jellyfinSettings;

    public AdminService(JellywatchDbContext context, IMetadataResolutionService metadataService, IJellyfinApiClient jellyfinClient, IOptions<JellyfinSettings> jellyfinSettings)
    {
        _context = context;
        _metadataService = metadataService;
        _jellyfinClient = jellyfinClient;
        _jellyfinSettings = jellyfinSettings.Value;
    }

    private async Task<bool> IsAdminAsync(int? currentUserId)
    {
        if (!currentUserId.HasValue) return false;
        return (await _context.Users.FindAsync(currentUserId.Value))?.IsAdmin == true;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<UserDto>>> GetUsersAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<List<UserDto>>.Fail("Forbidden", 403);

        var users = await _context.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                JellyfinUserId = u.JellyfinUserId,
                IsAdmin = u.IsAdmin,
                AvatarUrl = u.AvatarUrl,
                PreferredLanguage = u.PreferredLanguage,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<UserDto>>.Ok(users);
    }

    public async Task<ServiceResult<object>> DeleteUserAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        if (id == currentUserId)
            return ServiceResult<object>.Fail("Cannot delete your own user account.", 400);

        var user = await _context.Users.FindAsync(id);
        if (user is null) return ServiceResult<object>.Fail("User not found", 404);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return ServiceResult<object>.Ok(new { message = $"User \"{user.Username}\" deleted." });
    }

    public async Task<ServiceResult<List<ProfileDto>>> GetAllProfilesAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<List<ProfileDto>>.Fail("Forbidden", 403);

        var profiles = await _context.Profiles
            .OrderBy(p => p.DisplayName)
            .Select(p => new ProfileDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                JellyfinUserId = p.JellyfinUserId,
                IsJoint = p.IsJoint,
                UserId = p.UserId,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<ProfileDto>>.Ok(profiles);
    }

    // ── Jellyfin Users ───────────────────────────────────────────────────────

    public async Task<ServiceResult<object>> GetJellyfinUsersAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var jellyfinUsers = await _jellyfinClient.GetUsersAsync();
        var existingJellyfinIds = await _context.Profiles
            .Select(p => p.JellyfinUserId)
            .ToListAsync();

        var result = jellyfinUsers.Select(u => new
        {
            u.Id,
            u.Name,
            u.IsAdministrator,
            AlreadyTracked = existingJellyfinIds.Contains(u.Id)
        });

        return ServiceResult<object>.Ok(result);
    }

    public async Task<ServiceResult<ProfileDto>> AddProfileFromJellyfinAsync(int? currentUserId, AddProfileRequest request)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<ProfileDto>.Fail("Forbidden", 403);

        if (string.IsNullOrWhiteSpace(request.JellyfinUserId) || string.IsNullOrWhiteSpace(request.DisplayName))
            return ServiceResult<ProfileDto>.Fail("JellyfinUserId and DisplayName are required.", 400);

        var exists = await _context.Profiles
            .AnyAsync(p => p.JellyfinUserId == request.JellyfinUserId);

        if (exists)
            return ServiceResult<ProfileDto>.Fail("A profile for this Jellyfin user already exists.", 409);

        var profile = new Profile
        {
            JellyfinUserId = request.JellyfinUserId,
            DisplayName = request.DisplayName,
            IsJoint = false,
            UserId = null
        };

        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        return ServiceResult<ProfileDto>.Ok(new ProfileDto
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            JellyfinUserId = profile.JellyfinUserId,
            IsJoint = profile.IsJoint,
            UserId = profile.UserId,
            CreatedAt = profile.CreatedAt
        });
    }

    // ── Import Queue ─────────────────────────────────────────────────────────

    public async Task<ServiceResult<PagedResult<ImportQueueItemDto>>> GetImportQueueAsync(int? currentUserId, int page, int pageSize)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<PagedResult<ImportQueueItemDto>>.Fail("Forbidden", 403);

        var query = _context.ImportQueueItems.OrderByDescending(i => i.CreatedAt);
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ImportQueueItemDto
            {
                Id = i.Id,
                JellyfinItemId = i.JellyfinItemId,
                MediaType = i.MediaType.ToString(),
                Priority = i.Priority,
                Status = i.Status.ToString(),
                RetryCount = i.RetryCount,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<PagedResult<ImportQueueItemDto>>.Ok(new PagedResult<ImportQueueItemDto>
        {
            Data = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── Media Library ────────────────────────────────────────────────────────

    public async Task<ServiceResult<PagedResult<MediaLibraryItemDto>>> GetMediaLibraryAsync(int? currentUserId, int page, int pageSize)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<PagedResult<MediaLibraryItemDto>>.Fail("Forbidden", 403);

        var query = _context.MediaItems.OrderBy(m => m.Title);
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaLibraryItemDto
            {
                Id = m.Id,
                Title = m.Title,
                MediaType = m.MediaType.ToString(),
                PosterPath = m.PosterPath,
                ReleaseDate = m.ReleaseDate,
                Status = m.Status,
                TmdbId = m.TmdbId,
                TvMazeId = m.TvMazeId,
                ImdbId = m.ImdbId,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<PagedResult<MediaLibraryItemDto>>.Ok(new PagedResult<MediaLibraryItemDto>
        {
            Data = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ServiceResult> DeleteMediaItemAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult.Fail("Forbidden", 403);

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return ServiceResult.Fail("Not found", 404);

        var libraryItems = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId == id)
            .ToListAsync();

        foreach (var li in libraryItems)
            li.MediaItemId = null;

        _context.MediaItems.Remove(mediaItem);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<object>> RefreshMediaItemAsync(int? currentUserId, int id, RefreshMediaItemDto? dto)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return ServiceResult<object>.Fail("Not found", 404);

        var refreshImages = dto?.RefreshImages ?? true;
        await _metadataService.RefreshMediaItemAsync(id, dto?.ForceTmdbId, refreshImages);
        return ServiceResult<object>.Ok(new { message = "Refresh complete", title = mediaItem.Title });
    }

    public async Task<ServiceResult<object>> GetPosterOptionsAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var options = await _metadataService.GetPosterOptionsAsync(id);
        return ServiceResult<object>.Ok(options);
    }

    public async Task<ServiceResult<object>> SelectPosterAsync(int? currentUserId, int id, SelectPosterDto dto)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return ServiceResult<object>.Fail("Not found", 404);

        await _metadataService.SelectPosterAsync(id, dto.RemoteUrl);
        return ServiceResult<object>.Ok(new { message = "Poster selected" });
    }

    public async Task<ServiceResult<object>> GetLogoOptionsAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var options = await _metadataService.GetLogoOptionsAsync(id);
        return ServiceResult<object>.Ok(options);
    }

    public async Task<ServiceResult<object>> SelectLogoAsync(int? currentUserId, int id, SelectPosterDto dto)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return ServiceResult<object>.Fail("Not found", 404);

        await _metadataService.SelectLogoAsync(id, dto.RemoteUrl);
        return ServiceResult<object>.Ok(new { message = "Logo selected" });
    }

    public async Task<ServiceResult<object>> RefreshAllMetadataAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var count = await _metadataService.RefreshAllMetadataAsync();
        return ServiceResult<object>.Ok(new { message = $"Refreshed metadata for {count} items", count });
    }

    public async Task<ServiceResult<object>> RefreshAllImagesAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var count = await _metadataService.RefreshAllImagesAsync();
        return ServiceResult<object>.Ok(new { message = $"Refreshed images for {count} items", count });
    }

    // ── Profile purge ──────────────────────────────────────────────────────

    public async Task<ServiceResult<object>> PurgeProfileMediaAsync(int? currentUserId, int profileId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return ServiceResult<object>.Fail("Profile not found", 404);

        var watchStates = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == profileId)
            .ToListAsync();
        _context.ProfileWatchStates.RemoveRange(watchStates);

        var watchEvents = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId)
            .ToListAsync();
        _context.WatchEvents.RemoveRange(watchEvents);

        var blocks = await _context.ProfileMediaBlocks
            .Where(b => b.ProfileId == profileId)
            .ToListAsync();
        _context.ProfileMediaBlocks.RemoveRange(blocks);

        await _context.SaveChangesAsync();

        return ServiceResult<object>.Ok(new
        {
            message = $"Purged all media data for profile {profile.DisplayName}",
            watchStatesRemoved = watchStates.Count,
            watchEventsRemoved = watchEvents.Count,
            blocksRemoved = blocks.Count
        });
    }

    // ── Profile delete ────────────────────────────────────────────────────────

    public async Task<ServiceResult<object>> DeleteProfileAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Forbidden", 403);

        var profile = await _context.Profiles.FindAsync(id);
        if (profile is null) return ServiceResult<object>.Fail("Profile not found", 404);

        // Prevent deleting own primary profile
        if (currentUserId.HasValue)
        {
            var currentUser = await _context.Users.FindAsync(currentUserId.Value);
            if (currentUser is not null && profile.JellyfinUserId == currentUser.JellyfinUserId && profile.UserId == currentUser.Id)
                return ServiceResult<object>.Fail("Cannot delete your own primary profile.", 400);
        }

        var watchStates = await _context.ProfileWatchStates.Where(ws => ws.ProfileId == id).ToListAsync();
        _context.ProfileWatchStates.RemoveRange(watchStates);

        var watchEvents = await _context.WatchEvents.Where(e => e.ProfileId == id).ToListAsync();
        _context.WatchEvents.RemoveRange(watchEvents);

        var notes = await _context.ProfileNotes.Where(n => n.ProfileId == id).ToListAsync();
        _context.ProfileNotes.RemoveRange(notes);

        var blocks = await _context.ProfileMediaBlocks.Where(b => b.ProfileId == id).ToListAsync();
        _context.ProfileMediaBlocks.RemoveRange(blocks);

        var propagationRules = await _context.PropagationRules
            .Where(r => r.SourceProfileId == id || r.TargetProfileId == id)
            .ToListAsync();
        _context.PropagationRules.RemoveRange(propagationRules);

        _context.Profiles.Remove(profile);
        await _context.SaveChangesAsync();

        // Also delete the linked user if they have one (and it's not the current user)
        if (profile.UserId.HasValue && profile.UserId.Value != currentUserId)
        {
            var linkedUser = await _context.Users.FindAsync(profile.UserId.Value);
            if (linkedUser is not null)
            {
                _context.Users.Remove(linkedUser);
                await _context.SaveChangesAsync();
            }
        }

        return ServiceResult<object>.Ok(new { message = $"Profile \"{profile.DisplayName}\" deleted." });
    }

    // ── Create user for profile ───────────────────────────────────────────────

    public async Task<ServiceResult<UserDto>> CreateUserForProfileAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<UserDto>.Fail("Forbidden", 403);

        var profile = await _context.Profiles.FindAsync(id);
        if (profile is null) return ServiceResult<UserDto>.Fail("Profile not found", 404);

        if (profile.UserId.HasValue)
            return ServiceResult<UserDto>.Fail("This profile already has a linked user.", 409);

        // Check no user already exists for this JellyfinUserId
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.JellyfinUserId == profile.JellyfinUserId);
        if (existingUser is not null)
        {
            profile.UserId = existingUser.Id;
            await _context.SaveChangesAsync();
            return ServiceResult<UserDto>.Ok(new UserDto
            {
                Id = existingUser.Id,
                Username = existingUser.Username,
                JellyfinUserId = existingUser.JellyfinUserId,
                IsAdmin = existingUser.IsAdmin,
                AvatarUrl = existingUser.AvatarUrl,
                PreferredLanguage = existingUser.PreferredLanguage,
                CreatedAt = existingUser.CreatedAt
            });
        }

        // Resolve IsAdmin and AvatarUrl from Jellyfin if possible
        bool isAdmin = false;
        string? avatarUrl = null;
        try
        {
            var jellyfinUsers = await _jellyfinClient.GetUsersAsync();
            var match = jellyfinUsers.FirstOrDefault(u => u.Id == profile.JellyfinUserId);
            if (match is not null)
            {
                isAdmin = match.IsAdministrator;
            }
        }
        catch { /* silently ignore Jellyfin lookup failures */ }

        var serverUrl = _jellyfinSettings.BaseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(serverUrl))
        {
            // Fall back to any existing user's server URL
            var anyUser = await _context.Users.FirstOrDefaultAsync(u => u.JellyfinServerUrl != null);
            serverUrl = anyUser?.JellyfinServerUrl ?? string.Empty;
        }

        var user = new User
        {
            JellyfinUserId = profile.JellyfinUserId,
            Username = profile.DisplayName,
            IsAdmin = isAdmin,
            AvatarUrl = avatarUrl,
            JellyfinServerUrl = serverUrl
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        profile.UserId = user.Id;
        await _context.SaveChangesAsync();

        return ServiceResult<UserDto>.Ok(new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            JellyfinUserId = user.JellyfinUserId,
            IsAdmin = user.IsAdmin,
            AvatarUrl = user.AvatarUrl,
            PreferredLanguage = user.PreferredLanguage,
            CreatedAt = user.CreatedAt
        });
    }

    // ── Blacklist ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<BlacklistedItemDto>>> GetBlacklistAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<List<BlacklistedItemDto>>.Fail("Forbidden", 403);

        var items = await _context.BlacklistedItems
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlacklistedItemDto
            {
                Id = b.Id,
                JellyfinItemId = b.JellyfinItemId,
                DisplayName = b.DisplayName,
                Reason = b.Reason,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<BlacklistedItemDto>>.Ok(items);
    }

    public async Task<ServiceResult<BlacklistedItemDto>> AddToBlacklistAsync(int? currentUserId, AddToBlacklistDto dto)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<BlacklistedItemDto>.Fail("Forbidden", 403);

        var existing = await _context.BlacklistedItems
            .FirstOrDefaultAsync(b => b.JellyfinItemId == dto.JellyfinItemId);

        if (existing is not null)
            return ServiceResult<BlacklistedItemDto>.Fail("Already blacklisted", 409);

        var item = new BlacklistedItem
        {
            JellyfinItemId = dto.JellyfinItemId,
            DisplayName = dto.DisplayName,
            Reason = dto.Reason
        };

        _context.BlacklistedItems.Add(item);

        // Also remove from import queue if pending
        var queueItems = await _context.ImportQueueItems
            .Where(q => q.JellyfinItemId == dto.JellyfinItemId)
            .ToListAsync();
        _context.ImportQueueItems.RemoveRange(queueItems);

        await _context.SaveChangesAsync();

        return ServiceResult<BlacklistedItemDto>.Ok(new BlacklistedItemDto
        {
            Id = item.Id,
            JellyfinItemId = item.JellyfinItemId,
            DisplayName = item.DisplayName,
            Reason = item.Reason,
            CreatedAt = item.CreatedAt
        });
    }

    public async Task<ServiceResult> RemoveFromBlacklistAsync(int? currentUserId, int id)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult.Fail("Forbidden", 403);

        var item = await _context.BlacklistedItems.FindAsync(id);
        if (item is null) return ServiceResult.Fail("Not found", 404);

        _context.BlacklistedItems.Remove(item);
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<List<AdminProfileBlockDto>>> GetAllProfileBlocksAsync(int? currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<List<AdminProfileBlockDto>>.Fail("Forbidden", 403);

        var blocks = await _context.ProfileMediaBlocks
            .Include(b => b.Profile)
            .Include(b => b.MediaItem)
                .ThenInclude(m => m.Translations)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new AdminProfileBlockDto
            {
                Id = b.Id,
                ProfileId = b.ProfileId,
                ProfileName = b.Profile.DisplayName,
                MediaItemId = b.MediaItemId,
                Title = b.MediaItem.Title,
                SpanishTitle = b.MediaItem.Translations
                    .Where(t => t.Language.StartsWith("es") && t.Title != null)
                    .Select(t => t.Title)
                    .FirstOrDefault(),
                MediaType = b.MediaItem.MediaType,
                BlockedAt = b.CreatedAt,
            })
            .ToListAsync();

        return ServiceResult<List<AdminProfileBlockDto>>.Ok(blocks);
    }
}