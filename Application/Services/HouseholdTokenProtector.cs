using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellywatch.Api.Application.Services;

public static partial class HouseholdTokenProtector
{
    public static string Create(string prefix, int bytes = 32) =>
        prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool IsValidCodeChallenge(string? value) =>
        value is not null && value.Length == 43 && Base64UrlRegex().IsMatch(value);

    public static bool IsValidVerifier(string? value) =>
        value is not null && value.Length is >= 43 and <= 128 && PkceVerifierRegex().IsMatch(value);

    public static bool VerifyS256(string verifier, string expectedChallenge)
    {
        var calculated = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(calculated),
            Encoding.ASCII.GetBytes(expectedChallenge));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlRegex();

    [GeneratedRegex("^[A-Za-z0-9._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PkceVerifierRegex();
}
