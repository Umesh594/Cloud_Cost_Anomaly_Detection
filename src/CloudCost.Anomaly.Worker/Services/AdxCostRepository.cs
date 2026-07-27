using System.Data;
using System.Globalization;
using System.Text;
using CloudCost.Anomaly.Core;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services;

public sealed class AdxCostRepository(IOptions<AnomalyWorkerOptions> options, ILogger<AdxCostRepository> logger) : IAdxCostRepository
{
    public async Task IngestCostsAsync(IReadOnlyCollection<CostRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0 || string.IsNullOrWhiteSpace(options.Value.AdxClusterUri))
        {
            return;
        }

        await ExecuteControlCommandAsync(BuildInlineIngestCommand(options.Value.CostTable, records.Select(ToCsv)), cancellationToken);
        logger.LogInformation("Ingested {Count} raw cost rows into ADX", records.Count);
    }

    public async Task IngestChargebackAsync(IReadOnlyCollection<ChargebackAllocation> allocations, CancellationToken cancellationToken)
    {
        if (allocations.Count == 0 || string.IsNullOrWhiteSpace(options.Value.AdxClusterUri))
        {
            return;
        }

        await ExecuteControlCommandAsync(BuildInlineIngestCommand("ChargebackDaily", allocations.Select(ToCsv)), cancellationToken);
        logger.LogInformation("Ingested {Count} chargeback rows into ADX", allocations.Count);
    }

    public async Task<IReadOnlyCollection<AnomalyScore>> QueryAnomaliesAsync(decimal minimumDelta, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.AdxClusterUri))
        {
            return [];
        }

        var kcsb = new KustoConnectionStringBuilder(options.Value.AdxClusterUri, options.Value.AdxDatabase)
            .WithAadAzureTokenCredentialsAuthentication(new Azure.Identity.DefaultAzureCredential());
        using var client = KustoClientFactory.CreateCslQueryProvider(kcsb);
        using var reader = await client.ExecuteQueryAsync(options.Value.AdxDatabase, BuildAnomalyQuery(minimumDelta), new ClientRequestProperties());

        var anomalies = new List<AnomalyScore>();
        while (reader.Read())
        {
            anomalies.Add(new AnomalyScore(
                Get<DateTime>(reader, "Timestamp"),
                Get<string>(reader, "Team"),
                Get<string>(reader, "ServiceName"),
                Convert.ToDecimal(Get<double>(reader, "ActualCost")),
                Convert.ToDecimal(Get<double>(reader, "ExpectedCost")),
                Get<double>(reader, "Severity"),
                Get<string>(reader, "Explanation")));
        }

        return anomalies;
    }

    private async Task ExecuteControlCommandAsync(string command, CancellationToken cancellationToken)
    {
        var kcsb = new KustoConnectionStringBuilder(options.Value.AdxClusterUri, options.Value.AdxDatabase)
            .WithAadAzureTokenCredentialsAuthentication(new Azure.Identity.DefaultAzureCredential());
        using var adminClient = KustoClientFactory.CreateCslAdminProvider(kcsb);
        using var _ = await adminClient.ExecuteControlCommandAsync(options.Value.AdxDatabase, command, new ClientRequestProperties());
    }

    private string BuildAnomalyQuery(decimal minimumDelta)
    {
        var minimumDeltaLiteral = minimumDelta.ToString(CultureInfo.InvariantCulture);
        return $$"""
        let minimumDelta = {{minimumDeltaLiteral}};
        ChargebackDaily
        | where Timestamp > ago(35d)
        | make-series ActualCost=sum(Cost) default=0 on Timestamp from startofday(ago(35d)) to startofday(now()) step 1d by Team, ServiceName
        | extend (Anomalies, Score, Baseline) = series_decompose_anomalies(ActualCost, 1.5, -1, 'linefit')
        | mv-expand Timestamp to typeof(datetime), ActualCost to typeof(double), Baseline to typeof(double), Score to typeof(double), Anomalies to typeof(double)
        | where Anomalies == 1 and ActualCost - Baseline >= minimumDelta
        | extend Severity = round(min_of(1.0, abs(Score) / 5.0), 3)
        | extend ExpectedCost = Baseline
        | extend Explanation = strcat(ServiceName, ' spend for ', Team, ' exceeded ADX seasonal baseline by ', tostring(round(ActualCost - Baseline, 2)))
        | project Timestamp, Team, ServiceName, ActualCost, ExpectedCost, Severity, Explanation
        | order by Severity desc
        """;
    }

    private static T Get<T>(IDataRecord record, string name)
    {
        return (T)record[name];
    }

    private static string BuildInlineIngestCommand(string table, IEnumerable<string> csvRows)
    {
        var builder = new StringBuilder();
        builder.Append(".ingest inline into table ").Append(table).AppendLine(" <|");
        foreach (var row in csvRows)
        {
            builder.AppendLine(row);
        }

        return builder.ToString();
    }

    private static string ToCsv(CostRecord record)
    {
        return string.Join(',',
            Csv(record.UsageDate.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            Csv(record.SubscriptionId),
            Csv(record.ResourceId),
            Csv(record.MeterCategory),
            Csv(record.ServiceName),
            Csv(record.Currency),
            record.PretaxCost.ToString(CultureInfo.InvariantCulture),
            Csv(string.Join(';', record.Tags.Select(tag => $"{tag.Key}={tag.Value}"))));
    }

    private static string ToCsv(ChargebackAllocation allocation)
    {
        return string.Join(',',
            Csv(allocation.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)),
            Csv(allocation.Team),
            Csv(allocation.SubscriptionId),
            Csv(allocation.ServiceName),
            allocation.Cost.ToString(CultureInfo.InvariantCulture),
            Csv(allocation.Currency),
            allocation.Confidence.ToString(CultureInfo.InvariantCulture),
            Csv(allocation.AttributionSource));
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
