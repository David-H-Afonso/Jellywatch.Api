namespace Jellywatch.Api.Application.Services;

// Daily scheduling and queued refreshes use one writer; HTTP requests never hold this gate.
public sealed class MetadataWriteGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
