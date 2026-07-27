namespace CloudCost.Anomaly.Core;

public sealed record AnomalyScore(
    DateTimeOffset Timestamp,
    string Team,
    string ServiceName,
    decimal ActualCost,
    decimal ExpectedCost,
    double Severity,
    string Explanation);
