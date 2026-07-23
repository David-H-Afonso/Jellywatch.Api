namespace Jellywatch.Api.Configuration;

public sealed class HouseholdIntegrationSettings
{
    public const string SectionName = "HouseholdIntegration";

    public string ClientId { get; set; } = "household";
    public string[] RedirectUris { get; set; } = Array.Empty<string>();
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthorizationCodeMinutes { get; set; } = 5;

    public bool IsRegisteredRedirect(string redirectUri) =>
        !string.IsNullOrWhiteSpace(redirectUri)
        && redirectUri.Length <= 2048
        && !redirectUri.Contains('*')
        && Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(parsed.Fragment)
        && RedirectUris.Any(value => string.Equals(value, redirectUri, StringComparison.Ordinal));
}

public static class HouseholdScopes
{
    public const string ProfileRead = "profile.read";
    public const string ActivityRead = "activity.read";
    public const string UpcomingRead = "upcoming.read";
    public const string StateWrite = "media.state.write";
    public const string RatingWrite = "media.rating.write";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        ProfileRead,
        ActivityRead,
        UpcomingRead,
        StateWrite,
        RatingWrite,
    };
}
