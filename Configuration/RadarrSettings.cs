namespace Jellywatch.Api.Configuration;

public class RadarrSettings
{
    public const string SectionName = "RadarrSettings";

    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
