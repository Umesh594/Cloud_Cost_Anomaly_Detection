using System.Collections.Concurrent;
using CloudCost.Anomaly.Core.Publishing;
using CloudCost.Anomaly.Worker.Services.Publishing;

namespace CloudCost.Anomaly.Tests;

/// <summary>An in-memory <see cref="IEventSink"/> test double that records every delivery it receives.</summary>
internal sealed class FakeEventSink(string name, int supportedSchemaVersion = int.MaxValue) : IEventSink
{
    private readonly ConcurrentQueue<SinkEvent> deliveries = new();
    private int failNextAttempts;

    public string Name { get; } = name;

    public int SupportedSchemaVersion { get; } = supportedSchemaVersion;

    public IReadOnlyCollection<SinkEvent> Deliveries => deliveries.ToArray();

    public int DeliveryCount => deliveries.Count;

    /// <summary>Makes the next <paramref name="attempts"/> calls to <see cref="PublishAsync"/> throw, to exercise retry behaviour.</summary>
    public void FailNextAttempts(int attempts) => Interlocked.Exchange(ref failNextAttempts, attempts);

    public Task PublishAsync(SinkEvent sinkEvent, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref failNextAttempts) >= 0)
        {
            throw new InvalidOperationException("Simulated transient sink failure.");
        }

        deliveries.Enqueue(sinkEvent);
        return Task.CompletedTask;
    }
}
