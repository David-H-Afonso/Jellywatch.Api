using System.Text.Json;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jellywatch.Api.Application.Services;

public sealed class PushNotificationService(
    JellywatchDbContext context,
    IWebPushSender sender,
    IOptions<WebPushSettings> options,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebPushSettings _settings = options.Value;

    public bool IsConfigured => _settings.Enabled && _settings.HasVapidConfiguration;
    public string? PublicKey => IsConfigured ? _settings.PublicKey.Trim() : null;

    public async Task<bool> GetSeasonNotificationsPreferenceAsync(int userId, CancellationToken cancellationToken)
    {
        return await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.NotifySeasonUpdates)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SetSeasonNotificationsPreferenceAsync(int userId, bool enabled, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null) throw new KeyNotFoundException("User not found.");
        user.NotifySeasonUpdates = enabled;
        await context.SaveChangesAsync(cancellationToken);
        return user.NotifySeasonUpdates;
    }

    public async Task UpsertSubscriptionAsync(int userId, PushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var endpoint = ValidateEndpoint(request.Endpoint);
        var p256dh = ValidateKey(request.P256dh, nameof(request.P256dh));
        var auth = ValidateKey(request.Auth, nameof(request.Auth));
        if (!IsConfigured) throw new InvalidOperationException("Push notifications are not enabled on this server.");
        var now = DateTime.UtcNow;
        var subscription = await context.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint, cancellationToken);
        if (subscription is not null && subscription.UserId != userId)
            throw new InvalidOperationException("This browser subscription belongs to another user.");
        if (subscription is null)
        {
            context.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth,
                DeviceName = NormalizeDeviceName(request.DeviceName), IsActive = true,
                CreatedAt = now, UpdatedAt = now
            });
        }
        else
        {
            subscription.P256dh = p256dh;
            subscription.Auth = auth;
            subscription.DeviceName = NormalizeDeviceName(request.DeviceName);
            subscription.IsActive = true;
            subscription.UpdatedAt = now;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateSubscriptionAsync(int userId, string endpoint, CancellationToken cancellationToken)
    {
        var subscription = await context.PushSubscriptions.FirstOrDefaultAsync(x => x.UserId == userId && x.Endpoint == endpoint, cancellationToken);
        if (subscription is null) return;
        subscription.IsActive = false;
        subscription.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsSubscriptionActiveAsync(int userId, string endpoint, CancellationToken cancellationToken) =>
        context.PushSubscriptions.AsNoTracking().AnyAsync(
            x => x.UserId == userId && x.Endpoint == endpoint && x.IsActive, cancellationToken);

    public async Task NotifyNewSeasonAsync(int mediaItemId, int seasonNumber, string seasonName, CancellationToken cancellationToken = default)
    {
        if (seasonNumber <= 0) return;
        var media = await context.MediaItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mediaItemId, cancellationToken);
        if (media is null) return;
        var eventKey = $"new-season:{media.TmdbId ?? mediaItemId}:{seasonNumber}";
        var due = DateTime.UtcNow;
        await QueueForFollowersAsync(mediaItemId, eventKey, "Nueva temporada", $"{media.Title}: {seasonName}", $"/series/{mediaItemId}", due, cancellationToken);
    }

    public async Task ScheduleSeasonPremiereAsync(int mediaItemId, int seasonNumber, string seasonName, string? firstEpisodeAirDate, CancellationToken cancellationToken = default)
    {
        if (seasonNumber <= 0) return;
        if (!DateOnly.TryParse(firstEpisodeAirDate, out var airDate)) return;
        var media = await context.MediaItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mediaItemId, cancellationToken);
        if (media is null) return;
        var madrid = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");
        // Announce on the premiere day in the morning, not at the episode's exact broadcast time.
        var premiereLocal = airDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified);
        var due = TimeZoneInfo.ConvertTimeToUtc(premiereLocal, madrid);
        var now = DateTime.UtcNow;
        if (due < now)
        {
            // If discovered on its air date after the morning delivery slot, still send today.
            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, madrid)) != airDate) return;
            due = now;
        }
        var eventKey = $"season-premiere:{media.TmdbId ?? mediaItemId}:{seasonNumber}";
        await QueueForFollowersAsync(mediaItemId, eventKey, "Estreno hoy", $"Hoy se estrena {seasonName} de {media.Title}", $"/series/{mediaItemId}", due, cancellationToken);
    }

    private async Task QueueForFollowersAsync(int mediaItemId, string eventKey, string title, string body, string url, DateTime dueAtUtc, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;
        // A user follows a series when any owned profile has explicitly started or completed
        // the series, one of its seasons, or one of its episodes.
        var followers = await context.ProfileWatchStates.AsNoTracking()
            .Where(state => state.MediaItemId == mediaItemId
                && (state.State == WatchState.InProgress || state.State == WatchState.Seen)
                && state.Profile.UserId != null
                && state.Profile.User != null
                && state.Profile.User.NotifySeasonUpdates)
            .Select(state => new { UserId = state.Profile.UserId!.Value, state.Profile.User!.PreferredLanguage })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (followers.Count == 0) return;

        var followerUserIds = followers.Select(follower => follower.UserId).Distinct().ToList();
        var subscriptions = await context.PushSubscriptions
            .Where(x => followerUserIds.Contains(x.UserId) && x.IsActive)
            .ToListAsync(cancellationToken);
        var deliveries = await context.PushNotificationDeliveries
            .Where(delivery => delivery.EventKey == eventKey && followerUserIds.Contains(delivery.UserId))
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            var language = followers.FirstOrDefault(follower => follower.UserId == subscription.UserId)?.PreferredLanguage;
            var localizedTitle = Localize(title, language);
            var localizedBody = Localize(body, language);
            var existing = deliveries.FirstOrDefault(
                x => x.PushSubscriptionId == subscription.Id && x.EventKey == eventKey);
            if (existing is not null)
            {
                // Keep pending premiere notices aligned if TMDB corrects the episode date;
                // never alter or resend a delivery that has already gone out.
                if (existing.Status == "Pending")
                {
                    existing.DueAtUtc = dueAtUtc;
                    existing.Title = localizedTitle;
                    existing.Body = localizedBody;
                    existing.Url = url;
                }
                continue;
            }
            context.PushNotificationDeliveries.Add(new PushNotificationDelivery
            {
                UserId = subscription.UserId,
                PushSubscriptionId = subscription.Id,
                EventKey = eventKey,
                Title = localizedTitle,
                Body = localizedBody,
                Url = url,
                DueAtUtc = dueAtUtc,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });
        }
        if (context.ChangeTracker.HasChanges())
        {
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception)
            {
                logger.LogDebug(exception, "A duplicate push event was prevented by the delivery key.");
                foreach (var entry in context.ChangeTracker.Entries<PushNotificationDelivery>().Where(x => x.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    private static string Localize(string value, string? language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            if (value == "Nueva temporada") return "New season";
            if (value == "Estreno hoy") return "Premieres today";
            if (value.StartsWith("Hoy se estrena ", StringComparison.Ordinal))
                return value.Replace("Hoy se estrena ", "Today ", StringComparison.Ordinal).Replace(" de ", " of ", StringComparison.Ordinal);
            if (value.StartsWith("Temporada ", StringComparison.Ordinal)) return value.Replace("Temporada ", "Season ", StringComparison.Ordinal);
        }
        return value;
    }

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return 0;
        var now = DateTime.UtcNow;
        var ids = await context.PushNotificationDeliveries.AsNoTracking()
            .Where(x => x.DueAtUtc <= now && x.AttemptCount < _settings.MaxAttempts
                && (x.Status == "Pending"
                    || (x.Status == "Failed" && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
                    || (x.Status == "Processing" && x.CreatedAt <= now.AddMinutes(-2))))
            .OrderBy(x => x.DueAtUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);
        var sentCount = 0;
        foreach (var id in ids)
        {
            var delivery = await context.PushNotificationDeliveries.Include(x => x.PushSubscription)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (delivery is null
                || delivery.Status is not ("Pending" or "Failed" or "Processing")
                || !delivery.PushSubscription.IsActive) continue;
            delivery.Status = "Processing";
            delivery.AttemptCount++;
            await context.SaveChangesAsync(cancellationToken);
            var payload = JsonSerializer.Serialize(new { title = delivery.Title, body = delivery.Body, url = delivery.Url, tag = delivery.EventKey }, JsonOptions);
            var result = await sender.SendAsync(delivery.PushSubscription, payload, cancellationToken);
            if (result.Succeeded)
            {
                delivery.Status = "Sent";
                delivery.SentAtUtc = DateTime.UtcNow;
                delivery.LastError = null;
                sentCount++;
            }
            else if (result.StatusCode is 404 or 410)
            {
                delivery.PushSubscription.IsActive = false;
                delivery.Status = "Failed";
                delivery.LastError = "Push subscription expired.";
                delivery.AttemptCount = _settings.MaxAttempts;
            }
            else
            {
                delivery.Status = "Failed";
                delivery.LastError = result.Error?.Length > 1000 ? result.Error[..1000] : result.Error;
                delivery.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Pow(2, Math.Min(delivery.AttemptCount, 8)));
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        return sentCount;
    }

    private static string ValidateEndpoint(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Push endpoint must be a valid HTTPS URL (max 2048 characters).");
        return text;
    }

    private static string ValidateKey(string value, string propertyName)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 512) throw new ArgumentException($"{propertyName} is required (max 512 characters).");
        return text;
    }

    private static string? NormalizeDeviceName(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > 200) throw new ArgumentException("Device name cannot exceed 200 characters.");
        return text;
    }
}
