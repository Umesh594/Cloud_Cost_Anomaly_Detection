namespace CloudCost.Anomaly.Core;

public sealed record TeamPolicy(
    string Team,
    IReadOnlyCollection<string> CostCenters,
    IReadOnlyCollection<string> Subscriptions,
    IReadOnlyCollection<string> ResourceGroupPrefixes,
    IReadOnlyCollection<string> ServiceHints);
