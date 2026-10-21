namespace CloudCost.Anomaly.Worker.Services.Publishing;

/// <summary>What the bounded fan-out channel does when ingestion produces events faster than sinks drain them.</summary>
public enum ChannelOverflowPolicy
{
    /// <summary>Discard the oldest queued event to make room for the newest one (favours freshness).</summary>
    DropOldest,

    /// <summary>Discard the incoming event, keeping everything already queued (favours FIFO fairness).</summary>
    DropNewest,

    /// <summary>Block the publisher until space is available (favours no data loss, at the cost of backpressure on the caller).</summary>
    Wait,
}

public sealed class EventPublishingOptions
{
    /// <summary>Maximum number of in-flight events buffered between ingestion and the fan-out publishers.</summary>
    public int ChannelCapacity { get; set; } = 2000;

    /// <summary>Documented overflow behaviour once <see cref="ChannelCapacity"/> is reached.</summary>
    public ChannelOverflowPolicy OverflowPolicy { get; set; } = ChannelOverflowPolicy.DropOldest;

    /// <summary>Upper bound on concurrent sink delivery attempts across all events and sinks.</summary>
    public int MaxConcurrentDeliveries { get; set; } = 4;

    /// <summary>How long a successfully delivered (sink, event id) pair is remembered for deduplication.</summary>
    public int IdempotencyRetentionMinutes { get; set; } = 1440;

    /// <summary>
    /// The sink names that are active. A sink implementation is onboarded purely by adding its name here
    /// (and registering the implementation in DI) - nothing in the pipeline branches on this list.
    /// </summary>
    public IReadOnlyList<string> EnabledSinks { get; set; } = [];

    public WebhookSinkOptions Webhook { get; set; } = new();

    public ArchiveSinkOptions Archive { get; set; } = new();

    public AdxEventSinkOptions Adx { get; set; } = new();
}

public sealed class WebhookSinkOptions
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Highest schema version this HTTP consumer understands; defaults to "no ceiling".</summary>
    public int SupportedSchemaVersion { get; set; } = int.MaxValue;
}

public sealed class ArchiveSinkOptions
{
    /// <summary>Directory (local path or a mounted blob/file-share path) that NDJSON archive files are written to.</summary>
    public string Directory { get; set; } = string.Empty;

    public int SupportedSchemaVersion { get; set; } = int.MaxValue;
}

public sealed class AdxEventSinkOptions
{
    /// <summary>ADX table that published events are archived into, separate from the raw cost/chargeback tables.</summary>
    public string Table { get; set; } = "PublishedEvents";

    public int SupportedSchemaVersion { get; set; } = int.MaxValue;
}
