using Jellywatch.Api.Contracts.External;

namespace Jellywatch.Api.Infrastructure.ExternalServices;

public interface IOmdbApiClient
{
    Task<OmdbResponse?> GetByImdbIdAsync(string imdbId);
    bool IsConfigured { get; }
}
