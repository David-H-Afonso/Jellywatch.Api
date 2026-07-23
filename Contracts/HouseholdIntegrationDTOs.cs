using System.ComponentModel.DataAnnotations;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Contracts;

public sealed class HouseholdAuthorizeRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string CodeChallengeMethod { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public int? ProfileId { get; set; }
    public bool Approved { get; set; } = true;
}

public sealed class HouseholdAuthorizeResponse
{
    public string RedirectUri { get; set; } = string.Empty;
}

public sealed class HouseholdTokenRequest
{
    public string GrantType { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? RedirectUri { get; set; }
    public string? Code { get; set; }
    public string? CodeVerifier { get; set; }
    public string? RefreshToken { get; set; }
}

public sealed class HouseholdTokenResponse
{
    public string TokenType { get; set; } = "Bearer";
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public int RefreshExpiresIn { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public HouseholdAccountDto Account { get; set; } = new();
}

public sealed class HouseholdRevokeRequest
{
    public string Token { get; set; } = string.Empty;
    public string? TokenTypeHint { get; set; }
}

public sealed class HouseholdMeResponse
{
    public string ConnectionId { get; set; } = string.Empty;
    public HouseholdAccountDto Account { get; set; } = new();
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public sealed class HouseholdAccountDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class HouseholdDashboardDto
{
    public HouseholdProfileSummaryDto Profile { get; set; } = new();
    public List<HouseholdActivityItemDto> Activity { get; set; } = new();
    public List<HouseholdUpcomingItemDto> Upcoming { get; set; } = new();
}

public sealed class HouseholdProfileSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TotalSeriesWatching { get; set; }
    public int TotalSeriesCompleted { get; set; }
    public int TotalMoviesSeen { get; set; }
    public int TotalEpisodesSeen { get; set; }
}

public sealed class HouseholdActivityItemDto
{
    public int EventId { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int? EpisodeId { get; set; }
    public string? EpisodeName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal? UserRating { get; set; }
}

public sealed class HouseholdUpcomingItemDto
{
    public int MediaItemId { get; set; }
    public int SeriesId { get; set; }
    public string SeriesTitle { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? EpisodeName { get; set; }
    public string AirDate { get; set; } = string.Empty;
    public string? AirTime { get; set; }
    public string? AirTimeUtc { get; set; }
    public int BatchCount { get; set; } = 1;
}

public sealed class HouseholdStateWriteDto
{
    [Required]
    [RegularExpression("^(movie|episode|season|series)$", ErrorMessage = "TargetType must be movie, episode, season or series.")]
    public string TargetType { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int TargetId { get; set; }

    [Required]
    public WatchState State { get; set; }

    public DateTime? Timestamp { get; set; }
}

public sealed class HouseholdRatingWriteDto
{
    [Required]
    [RegularExpression("^(movie|episode|season|series)$", ErrorMessage = "TargetType must be movie, episode, season or series.")]
    public string TargetType { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int TargetId { get; set; }

    [Range(typeof(decimal), "0", "10", ErrorMessage = "Rating must be between 0 and 10.")]
    public decimal? Rating { get; set; }

    // Required only for episode and season targets; it scopes the lookup to one series.
    [Range(1, int.MaxValue)]
    public int? SeriesId { get; set; }
}
