namespace CloudCost.Anomaly.Core;

public sealed class PrintQueueHealthAnalyzer
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed",
        "failed",
        "cancelled"
    };

    public PrintQueueHealth Analyze(
        IReadOnlyCollection<PrintJobTelemetry> jobs,
        DateTimeOffset now,
        TimeSpan? stuckAfter = null,
        double failureRateThresholdPct = 5.0,
        double latencyThresholdSeconds = 120.0)
    {
        if (jobs.Count == 0)
        {
            return new PrintQueueHealth(
                "unknown",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "Healthy",
                "No print jobs were observed in the analysis window.");
        }

        var normalized = Normalize(jobs).ToArray();
        var window = stuckAfter ?? TimeSpan.FromMinutes(15);
        var completedJobs = normalized.Where(job => string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToArray();
        var failedJobs = normalized.Where(job => string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase)).ToArray();
        var stuckJobs = normalized
            .Where(job => !TerminalStatuses.Contains(job.Status) && now - job.SubmittedAt >= window)
            .ToArray();
        var latencies = completedJobs
            .Where(job => job.CompletedAt is not null)
            .Select(job => (job.CompletedAt!.Value - job.SubmittedAt).TotalSeconds)
            .Where(seconds => seconds >= 0)
            .OrderBy(seconds => seconds)
            .ToArray();

        var failureRatePct = Math.Round(failedJobs.Length * 100.0 / normalized.Length, 2);
        var p95Latency = PercentileNearestRank(latencies, 0.95);
        var largestPayloadMb = Math.Round(normalized.Max(job => job.PayloadBytes) / 1024.0 / 1024.0, 2);
        var healthState = failureRatePct >= failureRateThresholdPct || p95Latency >= latencyThresholdSeconds || stuckJobs.Length > 0
            ? "Degraded"
            : "Healthy";

        var queueHash = DataGovernancePolicy.StableHash(normalized[0].QueueName);
        return new PrintQueueHealth(
            queueHash,
            normalized.Length,
            completedJobs.Length,
            failedJobs.Length,
            stuckJobs.Length,
            failureRatePct,
            Math.Round(p95Latency, 2),
            largestPayloadMb,
            healthState,
            $"queue={queueHash}; failed={failedJobs.Length}; stuck={stuckJobs.Length}; p95LatencySeconds={p95Latency:F2}; largestPayloadMb={largestPayloadMb:F2}");
    }

    public IReadOnlyCollection<PrintJobTelemetry> Normalize(IReadOnlyCollection<PrintJobTelemetry> jobs)
    {
        return jobs.Select(job => job with
        {
            TenantId = DataGovernancePolicy.StableHash(job.TenantId),
            PrinterId = DataGovernancePolicy.StableHash(job.PrinterId),
            QueueName = DataGovernancePolicy.StableHash(job.QueueName),
            UserPrincipalName = DataGovernancePolicy.StableHash(job.UserPrincipalName),
            DocumentName = "[redacted]",
            ClientOs = NormalizeClientOs(job.ClientOs),
            ConnectorVersion = NormalizeConnectorVersion(job.ConnectorVersion)
        }).ToArray();
    }

    private static double PercentileNearestRank(IReadOnlyList<double> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(percentile * orderedValues.Count);
        return orderedValues[Math.Clamp(rank - 1, 0, orderedValues.Count - 1)];
    }

    private static string NormalizeClientOs(string clientOs)
    {
        if (clientOs.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        if (clientOs.Contains("macOS", StringComparison.OrdinalIgnoreCase))
        {
            return "macOS";
        }

        return "Other";
    }

    private static string NormalizeConnectorVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : "unknown";
    }
}
