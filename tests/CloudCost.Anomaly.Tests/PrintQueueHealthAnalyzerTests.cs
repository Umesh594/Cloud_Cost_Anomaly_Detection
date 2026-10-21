using CloudCost.Anomaly.Core;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class PrintQueueHealthAnalyzerTests
{
    [Fact]
    public void Analyze_FlagsDegradedQueueFromFailuresStuckJobsAndLatency()
    {
        var now = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var jobs = Enumerable.Range(0, 10)
            .Select(i => Job(
                now.AddMinutes(-20 - i),
                i switch
                {
                    0 or 1 => "failed",
                    2 or 3 or 4 => "processing",
                    _ => "completed"
                },
                i >= 5 ? now.AddMinutes(-20 - i).AddSeconds(i == 9 ? 240 : 45) : null,
                payloadBytes: i == 9 ? 8 * 1024 * 1024 : 1024 * 1024))
            .ToArray();

        var health = new PrintQueueHealthAnalyzer().Analyze(jobs, now);

        Assert.Equal("Degraded", health.HealthState);
        Assert.Equal(10, health.TotalJobs);
        Assert.Equal(5, health.CompletedJobs);
        Assert.Equal(2, health.FailedJobs);
        Assert.Equal(3, health.StuckJobs);
        Assert.Equal(20, health.FailureRatePct);
        Assert.Equal(240, health.P95QueueLatencySeconds);
        Assert.Equal(8, health.LargestPayloadMb);
    }

    [Fact]
    public void Normalize_RedactsUserDocumentTenantPrinterAndQueueIdentifiers()
    {
        var job = Job(
            DateTimeOffset.Parse("2026-08-23T10:00:00Z"),
            "completed",
            DateTimeOffset.Parse("2026-08-23T10:00:30Z"),
            1024);

        var normalized = new PrintQueueHealthAnalyzer().Normalize([job]).Single();

        Assert.NotEqual(job.TenantId, normalized.TenantId);
        Assert.NotEqual(job.PrinterId, normalized.PrinterId);
        Assert.NotEqual(job.QueueName, normalized.QueueName);
        Assert.NotEqual(job.UserPrincipalName, normalized.UserPrincipalName);
        Assert.Equal("[redacted]", normalized.DocumentName);
        Assert.Equal("Windows", normalized.ClientOs);
        Assert.Equal("2.8", normalized.ConnectorVersion);
        Assert.Equal(job.PageCount, normalized.PageCount);
    }

    [Fact]
    public void Analyze_KeepsHealthyQueueBelowFailureAndLatencyThresholds()
    {
        var now = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var jobs = Enumerable.Range(0, 8)
            .Select(i => Job(now.AddMinutes(-5), "completed", now.AddMinutes(-5).AddSeconds(30 + i), 512 * 1024))
            .ToArray();

        var health = new PrintQueueHealthAnalyzer().Analyze(jobs, now);

        Assert.Equal("Healthy", health.HealthState);
        Assert.Equal(8, health.TotalJobs);
        Assert.Equal(0, health.FailedJobs);
        Assert.Equal(0, health.StuckJobs);
        Assert.True(health.P95QueueLatencySeconds < 120);
    }

    private static PrintJobTelemetry Job(DateTimeOffset submittedAt, string status, DateTimeOffset? completedAt, long payloadBytes)
    {
        return new PrintJobTelemetry(
            "tenant-contoso",
            "printer-floor-7-west",
            "finance-print-queue",
            "alex.wilber@contoso.com",
            "PayrollExport-August.pdf",
            submittedAt,
            completedAt,
            status,
            12,
            payloadBytes,
            "Windows 11 Enterprise 23H2",
            "2.8.17");
    }
}
