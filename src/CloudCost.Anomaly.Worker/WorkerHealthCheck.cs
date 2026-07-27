using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CloudCost.Anomaly.Worker;

public sealed class WorkerHealthCheck(WorkerHealthState state, Microsoft.Extensions.Options.IOptions<AnomalyWorkerOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pollInterval = TimeSpan.FromMinutes(Math.Max(5, options.Value.PollIntervalMinutes));
        var staleAfter = pollInterval + TimeSpan.FromMinutes(10);
        var data = new Dictionary<string, object>
        {
            ["lastStartedAt"] = state.LastStartedAt?.ToString("O") ?? "never",
            ["lastSucceededAt"] = state.LastSucceededAt?.ToString("O") ?? "never",
            ["lastFailedAt"] = state.LastFailedAt?.ToString("O") ?? "never"
        };

        if (state.LastError is not null)
        {
            data["lastError"] = state.LastError;
        }

        if (state.LastSucceededAt is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Worker has not completed a successful cycle yet.", data: data));
        }

        if (now - state.LastSucceededAt > staleAfter)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Worker has not completed a successful cycle within the expected interval.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Worker completed a recent cycle.", data));
    }
}
