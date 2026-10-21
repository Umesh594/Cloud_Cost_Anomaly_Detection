using CloudCost.Anomaly.Core;
using CloudCost.Anomaly.Core.Publishing;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class EventEnvelopeFactoryTests
{
    [Fact]
    public void FromAnomaly_SameBusinessEvent_ProducesTheSameIdempotencyKey()
    {
        var anomaly = new AnomalyScore(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), "payments", "AKS", 500m, 100m, 0.9, "spend spike");

        var first = EventEnvelopeFactory.FromAnomaly(anomaly);
        var second = EventEnvelopeFactory.FromAnomaly(anomaly with { Explanation = "a different message, re-ingested on retry" });

        // The idempotency key must be derived from business identity (team/service/timestamp), not the
        // full payload, so a worker-cycle retry that recomputes the same anomaly still dedupes correctly
        // even if free-text fields like the explanation differ slightly between computations.
        Assert.Equal(first.EventId, second.EventId);
    }

    [Fact]
    public void FromAnomaly_DifferentService_ProducesDifferentIdempotencyKeys()
    {
        var timestamp = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var first = EventEnvelopeFactory.FromAnomaly(new AnomalyScore(timestamp, "payments", "AKS", 500m, 100m, 0.9, "x"));
        var second = EventEnvelopeFactory.FromAnomaly(new AnomalyScore(timestamp, "payments", "Storage", 500m, 100m, 0.9, "x"));

        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Fact]
    public void FromAnomaly_ProducesEventAtCurrentSchemaVersion()
    {
        var anomaly = new AnomalyScore(DateTimeOffset.UtcNow, "payments", "AKS", 500m, 100m, 0.9, "x");
        var catalog = EventSchemaCatalog.CreateDefault();

        var sinkEvent = EventEnvelopeFactory.FromAnomaly(anomaly);

        Assert.Equal(EventSchemaCatalog.CostAnomalyEventType, sinkEvent.EventType);
        Assert.Equal(catalog.Get(EventSchemaCatalog.CostAnomalyEventType).CurrentVersion, sinkEvent.SchemaVersion);
    }

    [Fact]
    public void FromChargebackAllocation_MapsRenamedV2Fields()
    {
        var allocation = new ChargebackAllocation(new DateOnly(2026, 8, 1), "payments", "sub-a", "AKS", 500m, "USD", 0.88, "policy:subscription");

        var sinkEvent = EventEnvelopeFactory.FromChargebackAllocation(allocation);

        Assert.Equal(0.88, sinkEvent.Fields["attributionConfidence"]);
        Assert.Equal("policy:subscription", sinkEvent.Fields["attributionDetail"]);
    }
}
