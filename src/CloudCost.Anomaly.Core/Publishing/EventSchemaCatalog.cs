namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// A registry of <see cref="EventSchema"/> definitions keyed by event type. New event types are
/// onboarded by registering an additional <see cref="EventSchema"/> instance; nothing in the fan-out
/// publisher switches on event type by name.
/// </summary>
public sealed class EventSchemaCatalog
{
    private readonly Dictionary<string, EventSchema> schemas;

    public EventSchemaCatalog(IEnumerable<EventSchema> schemas)
    {
        this.schemas = schemas.ToDictionary(s => s.EventType, StringComparer.OrdinalIgnoreCase);
    }

    public EventSchema Get(string eventType) =>
        schemas.TryGetValue(eventType, out var schema)
            ? schema
            : throw new KeyNotFoundException($"No schema registered for event type '{eventType}'.");

    public bool TryGet(string eventType, out EventSchema schema)
    {
        var found = schemas.TryGetValue(eventType, out var value);
        schema = value!;
        return found;
    }

    public const string CostAnomalyEventType = "cost.anomaly";
    public const string ChargebackAllocationEventType = "chargeback.allocation";
    public const string OwnershipFindingEventType = "iac.ownership-finding";

    /// <summary>
    /// The schema catalog shipped with this service. <c>cost.anomaly</c> demonstrates additive,
    /// backward-compatible evolution (an optional field added in v2); <c>chargeback.allocation</c>
    /// demonstrates a breaking rename (a required field retired and replaced in v2) so compatibility
    /// checks have a real failure case to catch, not just a version number comment.
    /// </summary>
    public static EventSchemaCatalog CreateDefault() => new(
    [
        new EventSchema(CostAnomalyEventType, currentVersion: 2,
        [
            new SchemaField("timestamp", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("team", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("serviceName", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("actualCost", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("expectedCost", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("severity", IsRequired: true, IntroducedInVersion: 1),
            // Added in v2: purely additive and optional, so v1-pinned sinks remain compatible.
            new SchemaField("explanation", IsRequired: false, IntroducedInVersion: 2),
        ]),
        new EventSchema(ChargebackAllocationEventType, currentVersion: 2,
        [
            new SchemaField("date", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("team", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("subscriptionId", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("serviceName", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("cost", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("currency", IsRequired: true, IntroducedInVersion: 1),
            // Retired in v2 in favour of attributionConfidence/attributionSource below: a sink still
            // pinned to v1 depends on these and must be flagged incompatible, not silently degraded.
            new SchemaField("confidence", IsRequired: true, IntroducedInVersion: 1, RemovedInVersion: 2),
            new SchemaField("attributionSource", IsRequired: true, IntroducedInVersion: 1, RemovedInVersion: 2),
            new SchemaField("attributionConfidence", IsRequired: true, IntroducedInVersion: 2),
            new SchemaField("attributionDetail", IsRequired: true, IntroducedInVersion: 2),
        ]),
        new EventSchema(OwnershipFindingEventType, currentVersion: 1,
        [
            new SchemaField("filePath", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("resourceName", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("message", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("suggestedTeam", IsRequired: true, IntroducedInVersion: 1),
            new SchemaField("confidence", IsRequired: true, IntroducedInVersion: 1),
        ]),
    ]);
}
