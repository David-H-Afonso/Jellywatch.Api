namespace Jellywatch.Api.Domain.Entities;

public sealed class MetadataDailyCycle
{
    public string LocalDate { get; set; } = string.Empty;
    public int JobId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public BulkMetadataJob Job { get; set; } = null!;
}
