using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Application.Interfaces;

public interface IPushNotificationService
{
    bool IsConfigured { get; }
    string? PublicKey { get; }
    Task<bool> GetSeasonNotificationsPreferenceAsync(int userId, CancellationToken cancellationToken);
    Task<bool> SetSeasonNotificationsPreferenceAsync(int userId, bool enabled, CancellationToken cancellationToken);
    Task UpsertSubscriptionAsync(int userId, PushSubscriptionRequest request, CancellationToken cancellationToken);
    Task<bool> IsSubscriptionActiveAsync(int userId, string endpoint, CancellationToken cancellationToken);
    Task DeactivateSubscriptionAsync(int userId, string endpoint, CancellationToken cancellationToken);
    Task NotifyNewSeasonAsync(int mediaItemId, int seasonNumber, string seasonName, CancellationToken cancellationToken = default);
    Task ScheduleSeasonPremiereAsync(int mediaItemId, int seasonNumber, string seasonName, string? firstEpisodeAirDate, CancellationToken cancellationToken = default);
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

public sealed class PushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
}

public interface IWebPushSender
{
    Task<WebPushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken);
}

public sealed record WebPushSendResult(bool Succeeded, int? StatusCode = null, string? Error = null);
