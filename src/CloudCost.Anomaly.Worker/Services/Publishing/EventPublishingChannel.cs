using System.Threading.Channels;
using CloudCost.Anomaly.Core.Publishing;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services.Publishing;

/// <summary>
/// The bounded handoff between ingestion/anomaly-detection and the fan-out publishers. Bounding the
/// channel is the backpressure contract: a slow or unavailable set of sinks cannot grow worker memory
/// without limit, and the overflow policy for what happens when the bound is hit is an explicit,
/// documented, configurable choice rather than an accident of an unbounded queue.
/// </summary>
public sealed class EventPublishingChannel
{
    private readonly Channel<SinkEvent> channel;
    private long droppedWhenFullCount;

    public EventPublishingChannel(IOptions<EventPublishingOptions> options)
    {
        var configured = options.Value;
        var fullMode = configured.OverflowPolicy switch
        {
            ChannelOverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
            ChannelOverflowPolicy.DropNewest => BoundedChannelFullMode.DropNewest,
            ChannelOverflowPolicy.Wait => BoundedChannelFullMode.Wait,
            _ => throw new ArgumentOutOfRangeException(nameof(options), configured.OverflowPolicy, "Unsupported overflow policy."),
        };

        channel = Channel.CreateBounded<SinkEvent>(new BoundedChannelOptions(Math.Max(1, configured.ChannelCapacity))
        {
            FullMode = fullMode,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Events dropped because the channel was full and under the <see cref="ChannelOverflowPolicy.Wait"/> policy a non-blocking write was attempted.</summary>
    public long DroppedWhenFullCount => Interlocked.Read(ref droppedWhenFullCount);

    public ChannelReader<SinkEvent> Reader => channel.Reader;

    /// <summary>
    /// Enqueues an event without blocking. Under <see cref="ChannelOverflowPolicy.DropOldest"/> or
    /// <see cref="ChannelOverflowPolicy.DropNewest"/> this always succeeds (the channel itself evicts to
    /// make room); under <see cref="ChannelOverflowPolicy.Wait"/> it can return <c>false</c> when full.
    /// </summary>
    public bool TryPublish(SinkEvent sinkEvent)
    {
        if (channel.Writer.TryWrite(sinkEvent))
        {
            return true;
        }

        Interlocked.Increment(ref droppedWhenFullCount);
        return false;
    }

    public void Complete() => channel.Writer.TryComplete();
}
