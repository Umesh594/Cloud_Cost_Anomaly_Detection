namespace CloudCost.Anomaly.Core;

public sealed record PrintJobTelemetry(
    string TenantId,
    string PrinterId,
    string QueueName,
    string UserPrincipalName,
    string DocumentName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int PageCount,
    long PayloadBytes,
    string ClientOs,
    string ConnectorVersion);

public sealed record PrintQueueHealth(
    string QueueNameHash,
    int TotalJobs,
    int CompletedJobs,
    int FailedJobs,
    int StuckJobs,
    double FailureRatePct,
    double P95QueueLatencySeconds,
    double LargestPayloadMb,
    string HealthState,
    string Explanation);
