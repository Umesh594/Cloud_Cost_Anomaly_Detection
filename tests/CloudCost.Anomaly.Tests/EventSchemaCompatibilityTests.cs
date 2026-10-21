using CloudCost.Anomaly.Core.Publishing;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class EventSchemaCompatibilityTests
{
    [Fact]
    public void CheckCompatibility_AdditiveOptionalField_IsBackwardCompatibleWithOlderSink()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.CostAnomalyEventType);
        var v2Event = new SinkEvent("evt-1", EventSchemaCatalog.CostAnomalyEventType, 2, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["team"] = "payments",
            ["serviceName"] = "AKS",
            ["actualCost"] = 500m,
            ["expectedCost"] = 100m,
            ["severity"] = 0.9,
            ["explanation"] = "spend spike",
        });

        // A sink that only ever understood v1 (before "explanation" existed) is still compatible,
        // because the new field is optional - this is what makes the evolution backward compatible.
        var result = schema.CheckCompatibility(v2Event, sinkSupportedVersion: 1);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingRequiredFields);
        Assert.Contains("explanation", result.FieldsUnknownToSink);
    }

    [Fact]
    public void CheckCompatibility_RequiredFieldRetired_IsIncompatibleWithOlderSink()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.ChargebackAllocationEventType);
        var v2Event = new SinkEvent("evt-2", EventSchemaCatalog.ChargebackAllocationEventType, 2, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["date"] = "2026-08-01",
            ["team"] = "payments",
            ["subscriptionId"] = "abc123",
            ["serviceName"] = "AKS",
            ["cost"] = 500m,
            ["currency"] = "USD",
            ["attributionConfidence"] = 0.9,
            ["attributionDetail"] = "tag:team",
        });

        // v1 required "confidence" and "attributionSource", which v2 retired in favour of renamed
        // fields. A sink still pinned to v1 must be rejected, not silently handed incomplete data.
        var result = schema.CheckCompatibility(v2Event, sinkSupportedVersion: 1);

        Assert.False(result.IsCompatible);
        Assert.Contains("confidence", result.MissingRequiredFields);
        Assert.Contains("attributionSource", result.MissingRequiredFields);
    }

    [Fact]
    public void CheckCompatibility_SameVersionSink_IsFullyCompatible()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.ChargebackAllocationEventType);
        var v2Event = new SinkEvent("evt-3", EventSchemaCatalog.ChargebackAllocationEventType, 2, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["date"] = "2026-08-01",
            ["team"] = "payments",
            ["subscriptionId"] = "abc123",
            ["serviceName"] = "AKS",
            ["cost"] = 500m,
            ["currency"] = "USD",
            ["attributionConfidence"] = 0.9,
            ["attributionDetail"] = "tag:team",
        });

        var result = schema.CheckCompatibility(v2Event, sinkSupportedVersion: 2);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.FieldsUnknownToSink);
    }

    [Fact]
    public void CheckCompatibility_EventVersionNewerThanRegistry_IsRejected()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.CostAnomalyEventType);
        var futureEvent = new SinkEvent("evt-4", EventSchemaCatalog.CostAnomalyEventType, 99, DateTimeOffset.UtcNow, new Dictionary<string, object?>());

        var result = schema.CheckCompatibility(futureEvent, sinkSupportedVersion: 99);

        Assert.False(result.IsCompatible);
    }

    [Fact]
    public void CheckCompatibility_WrongEventType_Throws()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.CostAnomalyEventType);
        var wrongTypeEvent = new SinkEvent("evt-5", EventSchemaCatalog.ChargebackAllocationEventType, 1, DateTimeOffset.UtcNow, new Dictionary<string, object?>());

        Assert.Throws<InvalidOperationException>(() => schema.CheckCompatibility(wrongTypeEvent, sinkSupportedVersion: 1));
    }

    [Fact]
    public void ProjectToVersion_DropsFieldsNewerThanSink()
    {
        var schema = EventSchemaCatalog.CreateDefault().Get(EventSchemaCatalog.CostAnomalyEventType);
        var v2Event = new SinkEvent("evt-6", EventSchemaCatalog.CostAnomalyEventType, 2, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["team"] = "payments",
            ["explanation"] = "spend spike",
        });

        var projected = schema.ProjectToVersion(v2Event, sinkSupportedVersion: 1);

        Assert.True(projected.Fields.ContainsKey("team"));
        Assert.False(projected.Fields.ContainsKey("explanation"));
    }
}
