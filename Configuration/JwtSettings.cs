namespace Jellywatch.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = "ThisIsAVerySecureSecretKeyThatShouldBeChangedInProduction123456789";
    public string Issuer { get; set; } = "Jellywatch.Api";
    public string Audience { get; set; } = "Jellywatch.Client";
    public int ExpirationMinutes { get; set; } = 10080; // 7 days
}
