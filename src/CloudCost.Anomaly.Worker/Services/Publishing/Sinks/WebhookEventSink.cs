using System.Text;
using System.Text.Json;
using CloudCost.Anomaly.Core.Publishing;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services.Publishing.Sinks;

/// <summary>Delivers published events as an HTTP POST to a configured webhook endpoint.</summary>
public sealed class WebhookEventSink(HttpClient httpClient, IOptions<EventPublishingOptions> options, ILogger<WebhookEventSink> logger) : IEventSink
{
    public string Name => "webhook";

    public int SupportedSchemaVersion => options.Value.Webhook.SupportedSchemaVersion;

    public async Task PublishAsync(SinkEvent sinkEvent, CancellationToken cancellationToken)
    {
        var url = options.Value.Webhook.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogDebug("Webhook URL not configured; dropping event {EventId}", sinkEvent.EventId);
            return;
        }

        var json = JsonSerializer.Serialize(sinkEvent);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
