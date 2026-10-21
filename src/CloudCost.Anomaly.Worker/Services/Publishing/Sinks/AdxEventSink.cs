using System.Globalization;
using System.Text;
using System.Text.Json;
using CloudCost.Anomaly.Core.Publishing;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services.Publishing.Sinks;

/// <summary>
/// Archives published events into a dedicated ADX table (distinct from the raw cost/chargeback tables
/// used for anomaly detection), preserving the full field bag as JSON so the destination degrades
/// gracefully across schema versions rather than requiring a fixed column set per version.
/// </summary>
public sealed class AdxEventSink(IOptions<EventPublishingOptions> publishingOptions, IOptions<AnomalyWorkerOptions> workerOptions, ILogger<AdxEventSink> logger) : IEventSink
{
    public string Name => "adx";

    public int SupportedSchemaVersion => publishingOptions.Value.Adx.SupportedSchemaVersion;

    public async Task PublishAsync(SinkEvent sinkEvent, CancellationToken cancellationToken)
    {
        var clusterUri = workerOptions.Value.AdxClusterUri;
        if (string.IsNullOrWhiteSpace(clusterUri))
        {
            logger.LogDebug("ADX cluster not configured; skipping archival of event {EventId}", sinkEvent.EventId);
            return;
        }

        var database = workerOptions.Value.AdxDatabase;
        var kcsb = KustoConnections.BuildConnectionString(clusterUri, database);
        using var adminClient = KustoClientFactory.CreateCslAdminProvider(kcsb);
        var command = BuildIngestCommand(publishingOptions.Value.Adx.Table, sinkEvent);
        using var _ = await adminClient.ExecuteControlCommandAsync(database, command, new ClientRequestProperties()).WaitAsync(cancellationToken);
    }

    private static string BuildIngestCommand(string table, SinkEvent sinkEvent)
    {
        var fieldsJson = JsonSerializer.Serialize(sinkEvent.Fields);
        var row = string.Join(',',
            Csv(sinkEvent.EventId),
            Csv(sinkEvent.EventType),
            sinkEvent.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            Csv(sinkEvent.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            Csv(fieldsJson));

        var builder = new StringBuilder();
        builder.Append(".ingest inline into table ").Append(table).AppendLine(" <|");
        builder.AppendLine(row);
        return builder.ToString();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
