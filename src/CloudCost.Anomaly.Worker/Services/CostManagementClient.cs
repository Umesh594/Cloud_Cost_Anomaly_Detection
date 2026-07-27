using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using CloudCost.Anomaly.Core;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace CloudCost.Anomaly.Worker.Services;

public sealed class CostManagementClient : ICostManagementClient
{
    private static readonly string[] AzureManagementScopes = ["https://management.azure.com/.default"];
    private readonly HttpClient httpClient;
    private readonly TokenCredential credential;
    private readonly AnomalyWorkerOptions options;
    private readonly ILogger<CostManagementClient> logger;
    private readonly ResiliencePipeline<HttpResponseMessage> retryPipeline;

    public CostManagementClient(HttpClient httpClient, IOptions<AnomalyWorkerOptions> options, ILogger<CostManagementClient> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        this.httpClient = httpClient;
        credential = new DefaultAzureCredential();
        retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => (int)response.StatusCode is 408 or 429 or >= 500)
            })
            .Build();
    }

    public async Task<IReadOnlyCollection<CostRecord>> QueryDailyCostsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.BillingScope))
        {
            logger.LogWarning("No billing scope configured. Returning empty cost dataset.");
            return [];
        }

        using var response = await retryPipeline.ExecuteAsync(async ct =>
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(AzureManagementScopes), ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/{options.BillingScope}/providers/Microsoft.CostManagement/query?api-version=2023-11-01");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Content = new StringContent(BuildCostQuery(from, to), Encoding.UTF8, "application/json");
            return await httpClient.SendAsync(request, ct);
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseResponse(document);
    }

    private static string BuildCostQuery(DateTimeOffset from, DateTimeOffset to)
    {
        var payload = new
        {
            type = "ActualCost",
            timeframe = "Custom",
            timePeriod = new { from = from.UtcDateTime.ToString("yyyy-MM-dd"), to = to.UtcDateTime.ToString("yyyy-MM-dd") },
            dataset = new
            {
                granularity = "Daily",
                aggregation = new { totalCost = new { name = "PreTaxCost", function = "Sum" } },
                grouping = new object[]
                {
                    new { type = "Dimension", name = "SubscriptionId" },
                    new { type = "Dimension", name = "ResourceId" },
                    new { type = "Dimension", name = "MeterCategory" },
                    new { type = "Dimension", name = "ServiceName" }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyCollection<CostRecord> ParseResponse(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty("rows", out var rows))
        {
            return [];
        }

        var results = new List<CostRecord>();
        foreach (var row in rows.EnumerateArray())
        {
            var values = row.EnumerateArray().ToArray();
            if (values.Length < 7)
            {
                continue;
            }

            results.Add(new CostRecord(
                DateTimeOffset.ParseExact(values[1].GetString() ?? string.Empty, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                values[2].GetString() ?? "unknown",
                values[3].GetString() ?? "unknown",
                values[4].GetString() ?? "unknown",
                values[5].GetString() ?? "unknown",
                values[6].GetString() ?? "USD",
                values[0].GetDecimal(),
                new Dictionary<string, string>()));
        }

        return results;
    }
}
