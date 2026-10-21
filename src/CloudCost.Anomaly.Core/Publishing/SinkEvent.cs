namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// A schema-versioned, sink-agnostic event envelope produced by the ingestion/anomaly pipeline and
/// routed to one or more <c>IEventSink</c> implementations by the fan-out publisher.
/// </summary>
/// <param name="EventId">
/// A deterministic idempotency key derived from the business content of the event (not a random GUID),
/// so re-publishing the same logical event (e.g. after a worker cycle retry) produces the same id and
/// can be deduplicated by sinks that care about exactly-once effects.
/// </param>
/// <param name="EventType">The logical event type, used to look up the matching <see cref="EventSchema"/>.</param>
/// <param name="SchemaVersion">The schema version this event was produced under.</param>
/// <param name="OccurredAt">When the underlying business event occurred.</param>
/// <param name="Fields">The event payload as a flexible field bag, enabling schema evolution without a code change.</param>
public sealed record SinkEvent(
    string EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Fields);
