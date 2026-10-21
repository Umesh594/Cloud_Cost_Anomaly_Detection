using Azure.Identity;
using Kusto.Data;

namespace CloudCost.Anomaly.Worker.Services;

/// <summary>Shared Kusto connection-string construction so every ADX-facing client authenticates identically.</summary>
internal static class KustoConnections
{
    public static KustoConnectionStringBuilder BuildConnectionString(string clusterUri, string database) =>
        new KustoConnectionStringBuilder(clusterUri, database)
            .WithAadAzureTokenCredentialsAuthentication(new DefaultAzureCredential());
}
