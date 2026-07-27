using CloudCost.Anomaly.Core;

namespace CloudCost.Anomaly.Worker;

public sealed class OwnershipPolicyOptions
{
    public List<TeamPolicy> Teams { get; set; } = [];
}
