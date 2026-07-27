namespace CloudCost.Anomaly.Core;

public sealed record CostRecord(
    DateTimeOffset UsageDate,
    string SubscriptionId,
    string ResourceId,
    string MeterCategory,
    string ServiceName,
    string Currency,
    decimal PretaxCost,
    IReadOnlyDictionary<string, string> Tags);
