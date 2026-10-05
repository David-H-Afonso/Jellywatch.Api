namespace Jellywatch.Api.Contracts;

public sealed record BulkMetadataJobDto(int Id, string Status, int Total, int Processed,
    int Succeeded, int Failed, string? LastError);
