using System.Text.Json.Serialization;

namespace Jellywatch.Api.Contracts.External;

public class OmdbResponse
{
    [JsonPropertyName("Response")]
    public string? Response { get; set; }

    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("imdbRating")]
    public string? ImdbRating { get; set; }

    [JsonPropertyName("imdbVotes")]
    public string? ImdbVotes { get; set; }

    [JsonPropertyName("Metascore")]
    public string? Metascore { get; set; }

    [JsonPropertyName("Ratings")]
    public List<OmdbRating>? Ratings { get; set; }
}

public class OmdbRating
{
    [JsonPropertyName("Source")]
    public string? Source { get; set; }

    [JsonPropertyName("Value")]
    public string? Value { get; set; }
}
