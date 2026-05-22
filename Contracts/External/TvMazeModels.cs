using System.Text.Json.Serialization;

namespace Jellywatch.Api.Contracts.External;

public class TvMazeSearchResult
{
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("show")]
    public TvMazeShow? Show { get; set; }
}

public class TvMazeShow
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("premiered")]
    public string? Premiered { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("image")]
    public TvMazeImage? Image { get; set; }

    [JsonPropertyName("externals")]
    public TvMazeExternals? Externals { get; set; }

    [JsonPropertyName("network")]
    public TvMazeNetwork? Network { get; set; }

    [JsonPropertyName("webChannel")]
    public TvMazeNetwork? WebChannel { get; set; }

    [JsonPropertyName("schedule")]
    public TvMazeSchedule? Schedule { get; set; }
}

public class TvMazeImage
{
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("original")]
    public string? Original { get; set; }
}

public class TvMazeExternals
{
    [JsonPropertyName("tvrage")]
    public int? TvRage { get; set; }

    [JsonPropertyName("thetvdb")]
    public int? TheTvdb { get; set; }

    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }
}

public class TvMazeNetwork
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("country")]
    public TvMazeCountry? Country { get; set; }
}

public class TvMazeCountry
{
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

public class TvMazeSchedule
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

public class TvMazeEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("airdate")]
    public string? Airdate { get; set; }

    [JsonPropertyName("airtime")]
    public string? Airtime { get; set; }

    [JsonPropertyName("airstamp")]
    public string? Airstamp { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("image")]
    public TvMazeImage? Image { get; set; }
}
