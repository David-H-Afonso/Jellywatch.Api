using System.Text.Json.Serialization;

namespace Jellywatch.Api.Contracts.External;

public class TmdbSearchResponse<T>
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

public class TmdbTvSearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }
}

public class TmdbMovieSearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }
}

public class TmdbTvDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("number_of_seasons")]
    public int NumberOfSeasons { get; set; }

    [JsonPropertyName("number_of_episodes")]
    public int NumberOfEpisodes { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }

    [JsonPropertyName("networks")]
    public List<TmdbNetwork>? Networks { get; set; }

    [JsonPropertyName("seasons")]
    public List<TmdbSeasonSummary>? Seasons { get; set; }

    [JsonPropertyName("external_ids")]
    public TmdbExternalIds? ExternalIds { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbGenre>? Genres { get; set; }
}

public class TmdbMovieDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("original_language")]
    public string? OriginalLanguage { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("external_ids")]
    public TmdbExternalIds? ExternalIds { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbGenre>? Genres { get; set; }
}

public class TmdbSeasonSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }
}

public class TmdbSeasonDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("episodes")]
    public List<TmdbEpisodeDetails>? Episodes { get; set; }
}

public class TmdbEpisodeDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("still_path")]
    public string? StillPath { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }
}

public class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class TmdbNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class TmdbExternalIds
{
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("tvdb_id")]
    public int? TvdbId { get; set; }
}

public class TmdbTranslationsResponse
{
    [JsonPropertyName("translations")]
    public List<TmdbTranslation>? Translations { get; set; }
}

public class TmdbTranslation
{
    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; set; }

    [JsonPropertyName("iso_3166_1")]
    public string? Iso31661 { get; set; }

    [JsonPropertyName("data")]
    public TmdbTranslationData? Data { get; set; }
}

public class TmdbTranslationData
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
}

public class TmdbImageCollection
{
    [JsonPropertyName("posters")]
    public List<TmdbImage>? Posters { get; set; }

    [JsonPropertyName("backdrops")]
    public List<TmdbImage>? Backdrops { get; set; }

    [JsonPropertyName("stills")]
    public List<TmdbImage>? Stills { get; set; }

    [JsonPropertyName("logos")]
    public List<TmdbImage>? Logos { get; set; }
}

public class TmdbImage
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }
}

public class TmdbCreditsResponse
{
    [JsonPropertyName("cast")]
    public List<TmdbCastMember>? Cast { get; set; }
}

public class TmdbCastMember
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class TmdbAggregateCreditsResponse
{
    [JsonPropertyName("cast")]
    public List<TmdbAggregateCastMember>? Cast { get; set; }
}

public class TmdbAggregateCastMember
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("roles")]
    public List<TmdbRole>? Roles { get; set; }

    [JsonPropertyName("total_episode_count")]
    public int TotalEpisodeCount { get; set; }
}

public class TmdbRole
{
    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }
}

public class TmdbPersonCreditsResponse
{
    [JsonPropertyName("cast")]
    public List<TmdbPersonCastCredit>? Cast { get; set; }
}

public class TmdbPersonCastCredit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double? VoteAverage { get; set; }
}

public class TmdbPersonDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("biography")]
    public string? Biography { get; set; }

    [JsonPropertyName("known_for_department")]
    public string? KnownForDepartment { get; set; }
}
