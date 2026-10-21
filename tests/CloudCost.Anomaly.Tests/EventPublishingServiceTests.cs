using CloudCost.Anomaly.Core.Publishing;
using CloudCost.Anomaly.Worker.Services.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class EventPublishingServiceTests
{
    private static SinkEvent AnomalyEvent(string eventId = "evt-1") => new(
        eventId,
        EventSchemaCatalog.CostAnomalyEventType,
        2,
        DateTimeOffset.UtcNow,
        new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["team"] = "payments",
            ["serviceName"] = "AKS",
            ["actualCost"] = 500m,
            ["expectedCost"] = 100m,
            ["severity"] = 0.9,
            ["explanation"] = "spend spike",
        });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FansOutSingleEventToAllActiveSinks()
    {
        var sinkA = new FakeEventSink("a");
        var sinkB = new FakeEventSink("b");
        var channel = new EventPublishingChannel(Options.Create(new EventPublishingOptions { ChannelCapacity = 10 }));
        var registry = new EventSinkRegistry([sinkA, sinkB], Options.Create(new EventPublishingOptions { EnabledSinks = ["a", "b"] }));
        var service = new EventPublishingService(
            channel, registry, EventSchemaCatalog.CreateDefault(), new IdempotencyStore(),
            Options.Create(new EventPublishingOptions { MaxConcurrentDeliveries = 4, IdempotencyRetentionMinutes = 60 }),
            NullLogger<EventPublishingService>.Instance);

        channel.TryPublish(AnomalyEvent());
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sinkA.DeliveryCount == 1 && sinkB.DeliveryCount == 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, sinkA.DeliveryCount);
        Assert.Equal(1, sinkB.DeliveryCount);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateEventId_IsDeliveredOnlyOncePerSink()
    {
        var sink = new FakeEventSink("a");
        var channel = new EventPublishingChannel(Options.Create(new EventPublishingOptions { ChannelCapacity = 10 }));
        var registry = new EventSinkRegistry([sink], Options.Create(new EventPublishingOptions { EnabledSinks = ["a"] }));
        var service = new EventPublishingService(
            channel, registry, EventSchemaCatalog.CreateDefault(), new IdempotencyStore(),
            Options.Create(new EventPublishingOptions { MaxConcurrentDeliveries = 4, IdempotencyRetentionMinutes = 60 }),
            NullLogger<EventPublishingService>.Instance);

        // Simulates at-least-once redelivery of the same logical event (e.g. a worker-cycle retry).
        channel.TryPublish(AnomalyEvent("evt-dup"));
        channel.TryPublish(AnomalyEvent("evt-dup"));
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sink.DeliveryCount >= 1, TimeSpan.FromSeconds(5));
        await Task.Delay(200); // give a wrongly-duplicated delivery a chance to land
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, sink.DeliveryCount);
    }

    [Fact]
    public async Task ExecuteAsync_SinkIncompatibleWithSchema_NeverReceivesTheEvent()
    {
        // "webhook" is pinned to schema v1 of chargeback.allocation, which v2 broke (confidence/
        // attributionSource were retired). The dispatcher must reject the delivery rather than hand the
        // sink an incomplete payload.
        var legacySink = new FakeEventSink("legacy", supportedSchemaVersion: 1);
        var channel = new EventPublishingChannel(Options.Create(new EventPublishingOptions { ChannelCapacity = 10 }));
        var registry = new EventSinkRegistry([legacySink], Options.Create(new EventPublishingOptions { EnabledSinks = ["legacy"] }));
        var service = new EventPublishingService(
            channel, registry, EventSchemaCatalog.CreateDefault(), new IdempotencyStore(),
            Options.Create(new EventPublishingOptions { MaxConcurrentDeliveries = 4, IdempotencyRetentionMinutes = 60 }),
            NullLogger<EventPublishingService>.Instance);

        var v2Event = new SinkEvent("evt-v2", EventSchemaCatalog.ChargebackAllocationEventType, 2, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["date"] = "2026-08-01",
            ["team"] = "payments",
            ["subscriptionId"] = "sub-a",
            ["serviceName"] = "AKS",
            ["cost"] = 500m,
            ["currency"] = "USD",
            ["attributionConfidence"] = 0.9,
            ["attributionDetail"] = "tag:team",
        });

        channel.TryPublish(v2Event);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, legacySink.DeliveryCount);
    }

    [Fact]
    public async Task ExecuteAsync_TransientSinkFailure_IsRetriedAndEventuallySucceeds()
    {
        var sink = new FakeEventSink("a");
        sink.FailNextAttempts(2);
        var channel = new EventPublishingChannel(Options.Create(new EventPublishingOptions { ChannelCapacity = 10 }));
        var registry = new EventSinkRegistry([sink], Options.Create(new EventPublishingOptions { EnabledSinks = ["a"] }));
        var service = new EventPublishingService(
            channel, registry, EventSchemaCatalog.CreateDefault(), new IdempotencyStore(),
            Options.Create(new EventPublishingOptions { MaxConcurrentDeliveries = 4, IdempotencyRetentionMinutes = 60 }),
            NullLogger<EventPublishingService>.Instance);

        channel.TryPublish(AnomalyEvent());
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sink.DeliveryCount == 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, sink.DeliveryCount);
    }

    [Fact]
    public async Task ExecuteAsync_OneSinkThrowingRepeatedly_DoesNotPreventDeliveryToOtherSinks()
    {
        var brokenSink = new FakeEventSink("broken");
        brokenSink.FailNextAttempts(int.MaxValue / 2);
        var healthySink = new FakeEventSink("healthy");
        var channel = new EventPublishingChannel(Options.Create(new EventPublishingOptions { ChannelCapacity = 10 }));
        var registry = new EventSinkRegistry([brokenSink, healthySink], Options.Create(new EventPublishingOptions { EnabledSinks = ["broken", "healthy"] }));
        var service = new EventPublishingService(
            channel, registry, EventSchemaCatalog.CreateDefault(), new IdempotencyStore(),
            Options.Create(new EventPublishingOptions { MaxConcurrentDeliveries = 4, IdempotencyRetentionMinutes = 60 }),
            NullLogger<EventPublishingService>.Instance);

        channel.TryPublish(AnomalyEvent());
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => healthySink.DeliveryCount == 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, healthySink.DeliveryCount);
        Assert.Equal(0, brokenSink.DeliveryCount);
    }
}
