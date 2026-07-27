using CloudCost.Anomaly.Core;

namespace CloudCost.Anomaly.Worker.Services;

public interface IAlertPublisher
{
    Task PublishAsync(IEnumerable<AnomalyScore> anomalies, IEnumerable<TerraformTagFinding> tagFindings, CancellationToken cancellationToken);
}
