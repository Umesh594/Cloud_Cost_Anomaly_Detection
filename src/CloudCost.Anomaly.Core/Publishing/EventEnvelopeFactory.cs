using System.Globalization;

namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// Builds <see cref="SinkEvent"/> envelopes from domain objects, always at the schema catalog's current
/// version, and always with a deterministic idempotency key derived from the event's business identity
/// (never <c>Guid.NewGuid()</c>) so re-publishing the same logical event across retries or worker cycles
/// yields the same <see cref="SinkEvent.EventId"/>.
/// </summary>
public static class EventEnvelopeFactory
{
    public static SinkEvent FromAnomaly(AnomalyScore anomaly)
    {
        var fields = new Dictionary<string, object?>
        {
            ["timestamp"] = anomaly.Timestamp,
            ["team"] = anomaly.Team,
            ["serviceName"] = anomaly.ServiceName,
            ["actualCost"] = anomaly.ActualCost,
            ["expectedCost"] = anomaly.ExpectedCost,
            ["severity"] = anomaly.Severity,
            ["explanation"] = anomaly.Explanation,
        };

        var key = IdempotencyKey(
            EventSchemaCatalog.CostAnomalyEventType,
            anomaly.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            anomaly.Team,
            anomaly.ServiceName);

        return new SinkEvent(key, EventSchemaCatalog.CostAnomalyEventType, SchemaVersion: 2, anomaly.Timestamp, fields);
    }

    public static SinkEvent FromChargebackAllocation(ChargebackAllocation allocation)
    {
        var occurredAt = allocation.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fields = new Dictionary<string, object?>
        {
            ["date"] = allocation.Date.ToString("O", CultureInfo.InvariantCulture),
            ["team"] = allocation.Team,
            ["subscriptionId"] = allocation.SubscriptionId,
            ["serviceName"] = allocation.ServiceName,
            ["cost"] = allocation.Cost,
            ["currency"] = allocation.Currency,
            ["attributionConfidence"] = allocation.Confidence,
            ["attributionDetail"] = allocation.AttributionSource,
        };

        var key = IdempotencyKey(
            EventSchemaCatalog.ChargebackAllocationEventType,
            allocation.Date.ToString("O", CultureInfo.InvariantCulture),
            allocation.Team,
            allocation.SubscriptionId,
            allocation.ServiceName);

        return new SinkEvent(key, EventSchemaCatalog.ChargebackAllocationEventType, SchemaVersion: 2, occurredAt, fields);
    }

    public static SinkEvent FromOwnershipFinding(TerraformTagFinding finding, DateTimeOffset observedAt)
    {
        var fields = new Dictionary<string, object?>
        {
            ["filePath"] = finding.FilePath,
            ["resourceName"] = finding.ResourceName,
            ["message"] = finding.Message,
            ["suggestedTeam"] = finding.SuggestedTeam,
            ["confidence"] = finding.Confidence,
        };

        var key = IdempotencyKey(EventSchemaCatalog.OwnershipFindingEventType, finding.FilePath, finding.ResourceName, finding.Message);

        return new SinkEvent(key, EventSchemaCatalog.OwnershipFindingEventType, SchemaVersion: 1, observedAt, fields);
    }

    private static string IdempotencyKey(params string[] parts) =>
        DataGovernancePolicy.StableHash(string.Join('|', parts));
}
