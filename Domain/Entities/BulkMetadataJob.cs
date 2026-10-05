namespace Jellywatch.Api.Domain.Entities;

public sealed class BulkMetadataJob
{
    public int Id { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ActiveKey { get; set; } = "metadata";
    public string MediaItemIdsJson { get; set; } = "[]";
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
