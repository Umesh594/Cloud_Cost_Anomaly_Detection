using CloudCost.Anomaly.Core.Publishing;

namespace CloudCost.Anomaly.Worker.Services.Publishing;

/// <summary>
/// A publishing destination for <see cref="SinkEvent"/>s. New destinations are onboarded by adding an
/// implementation, registering it in DI, and listing its <see cref="Name"/> in the
/// <c>EventPublishing:EnabledSinks</c> configuration - no change to the dispatch pipeline
/// (<c>EventPublishingService</c>) or to the worker's core ingestion/anomaly-detection logic is required.
/// </summary>
public interface IEventSink
{
    /// <summary>The sink's unique, configuration-addressable name (e.g. "adx", "webhook", "archive").</summary>
    string Name { get; }

    /// <summary>
    /// The highest schema version this sink understands for any given event type. The fan-out publisher
    /// uses this to reject or gracefully project events the sink cannot safely consume.
    /// </summary>
    int SupportedSchemaVersion { get; }

    /// <summary>
    /// Delivers a single event. Implementations should throw on failure so the publisher's retry policy
    /// can act; they must honour <paramref name="cancellationToken"/> promptly on shutdown.
    /// </summary>
    Task PublishAsync(SinkEvent sinkEvent, CancellationToken cancellationToken);
}
