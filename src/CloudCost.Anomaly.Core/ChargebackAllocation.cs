namespace CloudCost.Anomaly.Core;

public sealed record ChargebackAllocation(
    DateOnly Date,
    string Team,
    string SubscriptionId,
    string ServiceName,
    decimal Cost,
    string Currency,
    double Confidence,
    string AttributionSource);
