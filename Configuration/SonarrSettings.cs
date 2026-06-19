namespace Jellywatch.Api.Configuration;

public class SonarrSettings
{
    public const string SectionName = "SonarrSettings";

    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
