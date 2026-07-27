using CloudCost.Anomaly.Core;

namespace CloudCost.Anomaly.Worker.Services;

public interface ICostManagementClient
{
    Task<IReadOnlyCollection<CostRecord>> QueryDailyCostsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
