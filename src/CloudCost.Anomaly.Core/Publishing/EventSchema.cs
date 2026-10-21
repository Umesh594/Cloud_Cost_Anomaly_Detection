namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// The full field-level version history for a single event type. This is the compatibility contract a
/// real schema registry would enforce: it lets the publisher reason about whether a sink pinned to an
/// older schema version can still safely consume events produced under a newer version, and lets the
/// publisher project an event down to the fields a sink actually understands.
/// </summary>
public sealed class EventSchema
{
    public EventSchema(string eventType, int currentVersion, IReadOnlyList<SchemaField> fields)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type must not be empty.", nameof(eventType));
        }

        if (currentVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersion), currentVersion, "Current version must be at least 1.");
        }

        if (fields.Count == 0)
        {
            throw new ArgumentException("A schema must declare at least one field.", nameof(fields));
        }

        EventType = eventType;
        CurrentVersion = currentVersion;
        Fields = fields;
    }

    public string EventType { get; }

    public int CurrentVersion { get; }

    public IReadOnlyList<SchemaField> Fields { get; }

    /// <summary>The names of fields that are mandatory as of the given schema version.</summary>
    public IReadOnlySet<string> RequiredFieldsAsOf(int version) =>
        Fields.Where(f => f.IsRequired && f.IsActiveAt(version)).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The names of all fields (required or optional) that exist as of the given schema version.</summary>
    public IReadOnlySet<string> AllFieldsAsOf(int version) =>
        Fields.Where(f => f.IsActiveAt(version)).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Checks whether <paramref name="sinkEvent"/>, produced under this schema's rules, can be safely
    /// consumed by a sink that only understands schema version <paramref name="sinkSupportedVersion"/>.
    ///
    /// Backward compatibility means: every field that was required as of the sink's understood version
    /// must still be present on the event. Fields introduced after the sink's version are never fatal -
    /// a compliant sink ignores fields it doesn't recognise - but they are reported so callers can log or
    /// alert on silent data loss at the sink boundary.
    /// </summary>
    public SchemaCompatibilityResult CheckCompatibility(SinkEvent sinkEvent, int sinkSupportedVersion)
    {
        if (!string.Equals(sinkEvent.EventType, EventType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Schema '{EventType}' cannot evaluate an event of type '{sinkEvent.EventType}'.");
        }

        if (sinkEvent.SchemaVersion > CurrentVersion)
        {
            // The event claims a schema version the registry has never seen - a producer/registry skew
            // that must never be silently accepted.
            return new SchemaCompatibilityResult(
                false,
                [],
                [$"event schema version {sinkEvent.SchemaVersion} exceeds the registry's known version {CurrentVersion}"]);
        }

        var requiredForSink = RequiredFieldsAsOf(sinkSupportedVersion);
        var missing = requiredForSink.Where(field => !sinkEvent.Fields.ContainsKey(field)).ToArray();

        var knownToSink = AllFieldsAsOf(sinkSupportedVersion);
        var unknownToSink = sinkEvent.Fields.Keys.Where(field => !knownToSink.Contains(field)).ToArray();

        return new SchemaCompatibilityResult(missing.Length == 0, missing, unknownToSink);
    }

    /// <summary>
    /// Projects an event down to only the fields a sink pinned to <paramref name="sinkSupportedVersion"/>
    /// understands, so a legacy or narrowly-scoped sink degrades gracefully instead of receiving fields it
    /// cannot interpret. Callers should run <see cref="CheckCompatibility"/> first; projection does not by
    /// itself guarantee the result still satisfies the sink's required fields.
    /// </summary>
    public SinkEvent ProjectToVersion(SinkEvent sinkEvent, int sinkSupportedVersion)
    {
        var allowed = AllFieldsAsOf(sinkSupportedVersion);
        var projectedFields = sinkEvent.Fields
            .Where(kv => allowed.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return sinkEvent with { Fields = projectedFields };
    }
}
