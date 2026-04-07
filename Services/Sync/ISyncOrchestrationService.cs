using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Services.Sync;

public interface ISyncOrchestrationService
{
    Task ProcessWatchEventAsync(int profileId, string jellyfinItemId, WatchEventType eventType, long positionTicks, SyncSource source);
    Task RunFullSyncAsync(int? profileId = null);
    Task ReconcileProfileAsync(int profileId);
}
