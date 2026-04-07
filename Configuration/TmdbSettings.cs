namespace Jellywatch.Api.Configuration;

public class TmdbSettings
{
    public const string SectionName = "TmdbSettings";

    public string? ApiKey { get; set; }
    public string PrimaryLanguage { get; set; } = "en-US";
    public string? FallbackLanguage { get; set; } = "es-ES";
    public int CacheDetailsTtlHours { get; set; } = 24;
    public int CacheImagesTtlDays { get; set; } = 7;
}
