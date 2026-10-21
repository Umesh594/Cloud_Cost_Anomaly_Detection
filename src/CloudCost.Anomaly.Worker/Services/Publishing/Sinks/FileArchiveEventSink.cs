using System.Text;
using System.Text.Json;
using CloudCost.Anomaly.Core.Publishing;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services.Publishing.Sinks;

/// <summary>
/// Archives published events as newline-delimited JSON, one file per (event type, UTC day), under a
/// configured directory. In AKS this is typically a mounted Azure Files/Blob volume, so the same code
/// path works unmodified whether the target is local disk in dev or blob storage in production.
/// </summary>
public sealed class FileArchiveEventSink(IOptions<EventPublishingOptions> options, ILogger<FileArchiveEventSink> logger) : IEventSink, IDisposable
{
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public string Name => "archive";

    public int SupportedSchemaVersion => options.Value.Archive.SupportedSchemaVersion;

    public async Task PublishAsync(SinkEvent sinkEvent, CancellationToken cancellationToken)
    {
        var directory = options.Value.Archive.Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            logger.LogDebug("Archive directory not configured; dropping event {EventId}", sinkEvent.EventId);
            return;
        }

        Directory.CreateDirectory(directory);
        var fileName = $"{sinkEvent.EventType}-{sinkEvent.OccurredAt.UtcDateTime:yyyy-MM-dd}.ndjson";
        var filePath = Path.Combine(directory, fileName);
        var line = JsonSerializer.Serialize(sinkEvent) + Environment.NewLine;

        // File.AppendAllTextAsync is not safe for concurrent callers against the same path; a single
        // in-process gate serializes writes from this sink instance (which is a singleton).
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(filePath, line, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public void Dispose() => writeGate.Dispose();
}
