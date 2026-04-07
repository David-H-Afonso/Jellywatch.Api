namespace Jellywatch.Api.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string DatabasePath { get; set; } = "../jellywatch.db";
    public bool EnableSensitiveDataLogging { get; set; }
}
