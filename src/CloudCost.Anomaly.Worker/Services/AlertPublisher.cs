using System.Text;
using System.Text.Json;
using CloudCost.Anomaly.Core;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services;

public sealed class AlertPublisher(HttpClient httpClient, IOptions<AnomalyWorkerOptions> options, ILogger<AlertPublisher> logger) : IAlertPublisher
{
    public async Task PublishAsync(IEnumerable<AnomalyScore> anomalies, IEnumerable<TerraformTagFinding> tagFindings, CancellationToken cancellationToken)
    {
        var payload = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            anomalies = anomalies.ToArray(),
            ownershipFindings = tagFindings.ToArray()
        };

        if (payload.anomalies.Length == 0 && payload.ownershipFindings.Length == 0)
        {
            logger.LogInformation("No anomaly or policy alerts to publish.");
            return;
        }

        logger.LogWarning("Publishing {AnomalyCount} anomalies and {FindingCount} IaC ownership findings", payload.anomalies.Length, payload.ownershipFindings.Length);

        if (string.IsNullOrWhiteSpace(options.Value.AlertWebhookUrl))
        {
            logger.LogInformation("Alert webhook not configured; payload retained in structured logs. {@Payload}", payload);
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        using var response = await httpClient.PostAsync(options.Value.AlertWebhookUrl, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
