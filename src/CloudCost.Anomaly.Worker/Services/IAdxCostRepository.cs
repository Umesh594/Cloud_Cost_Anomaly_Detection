using CloudCost.Anomaly.Core;

namespace CloudCost.Anomaly.Worker.Services;

public interface IAdxCostRepository
{
    Task IngestCostsAsync(IReadOnlyCollection<CostRecord> records, CancellationToken cancellationToken);
    Task IngestChargebackAsync(IReadOnlyCollection<ChargebackAllocation> allocations, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AnomalyScore>> QueryAnomaliesAsync(decimal minimumDelta, CancellationToken cancellationToken);
}
