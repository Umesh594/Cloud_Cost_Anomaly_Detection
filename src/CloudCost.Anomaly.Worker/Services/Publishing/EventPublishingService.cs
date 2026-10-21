using CloudCost.Anomaly.Core.Publishing;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace CloudCost.Anomaly.Worker.Services.Publishing;

/// <summary>
/// Drains the bounded fan-out channel and delivers each event to every active sink concurrently,
/// enforcing (in order): idempotent-delivery dedup, schema-compatibility rejection/projection, and
/// bounded-concurrency retry. A failing sink never blocks or fails delivery to any other sink for the
/// same event - each (event, sink) delivery is an independent unit of work.
/// </summary>
public sealed class EventPublishingService : BackgroundService
{
    private readonly EventPublishingChannel channel;
    private readonly EventSinkRegistry registry;
    private readonly EventSchemaCatalog schemaCatalog;
    private readonly IdempotencyStore idempotencyStore;
    private readonly EventPublishingOptions options;
    private readonly ILogger<EventPublishingService> logger;
    private readonly SemaphoreSlim concurrencyGate;
    private readonly ResiliencePipeline retryPipeline;

    public EventPublishingService(
        EventPublishingChannel channel,
        EventSinkRegistry registry,
        EventSchemaCatalog schemaCatalog,
        IdempotencyStore idempotencyStore,
        IOptions<EventPublishingOptions> options,
        ILogger<EventPublishingService> logger)
    {
        this.channel = channel;
        this.registry = registry;
        this.schemaCatalog = schemaCatalog;
        this.idempotencyStore = idempotencyStore;
        this.options = options.Value;
        this.logger = logger;
        concurrencyGate = new SemaphoreSlim(Math.Max(1, this.options.MaxConcurrentDeliveries));
        retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retention = TimeSpan.FromMinutes(Math.Max(1, options.IdempotencyRetentionMinutes));
        var inFlight = new List<Task>();
        var processedSinceEviction = 0;

        await foreach (var sinkEvent in channel.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var sink in registry.ActiveSinks)
            {
                inFlight.Add(DeliverAsync(sink, sinkEvent, retention, stoppingToken));
            }

            inFlight.RemoveAll(t => t.IsCompleted);

            if (++processedSinceEviction >= 200)
            {
                processedSinceEviction = 0;
                idempotencyStore.EvictExpired();
            }
        }

        await Task.WhenAll(inFlight);
    }

    private async Task DeliverAsync(IEventSink sink, SinkEvent sinkEvent, TimeSpan retention, CancellationToken cancellationToken)
    {
        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            if (!idempotencyStore.TryAcquire(sink.Name, sinkEvent.EventId, retention))
            {
                logger.LogDebug("Skipping duplicate delivery of {EventId} ({EventType}) to sink {Sink}", sinkEvent.EventId, sinkEvent.EventType, sink.Name);
                return;
            }

            if (!schemaCatalog.TryGet(sinkEvent.EventType, out var schema))
            {
                logger.LogWarning("No schema registered for event type {EventType}; dropping delivery to sink {Sink}", sinkEvent.EventType, sink.Name);
                return;
            }

            var compatibility = schema.CheckCompatibility(sinkEvent, sink.SupportedSchemaVersion);
            if (!compatibility.IsCompatible)
            {
                logger.LogWarning(
                    "Rejecting {EventType} event {EventId} for sink {Sink}: missing required field(s) [{Missing}]",
                    sinkEvent.EventType, sinkEvent.EventId, sink.Name, string.Join(',', compatibility.MissingRequiredFields));
                return;
            }

            if (compatibility.FieldsUnknownToSink.Count > 0)
            {
                logger.LogInformation(
                    "Sink {Sink} does not understand field(s) [{Unknown}] on {EventType} event {EventId}; projecting them out",
                    sink.Name, string.Join(',', compatibility.FieldsUnknownToSink), sinkEvent.EventType, sinkEvent.EventId);
            }

            var deliverable = schema.ProjectToVersion(sinkEvent, sink.SupportedSchemaVersion);

            await retryPipeline.ExecuteAsync(async ct => await sink.PublishAsync(deliverable, ct), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host is shutting down; nothing further to do with this delivery.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sink {Sink} failed to deliver {EventType} event {EventId} after retries", sink.Name, sinkEvent.EventType, sinkEvent.EventId);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }
}
