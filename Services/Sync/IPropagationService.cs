using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Services.Sync;

public interface IPropagationService
{
    Task PropagateStateChangeAsync(int sourceProfileId, int mediaItemId, int? episodeId, int? movieId, WatchState newState, DateTime? timestamp = null);
}
