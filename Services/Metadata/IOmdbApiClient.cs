using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Services.Metadata;

public interface IOmdbApiClient
{
    Task<OmdbResponse?> GetByImdbIdAsync(string imdbId);
    bool IsConfigured { get; }
}
