using CloudCost.Anomaly.Core;
using CloudCost.Anomaly.Core.Publishing;
using CloudCost.Anomaly.Worker.Services;
using CloudCost.Anomaly.Worker.Services.Publishing;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker;

public sealed class CostAnomalyWorker(
    ICostManagementClient costClient,
    IAdxCostRepository adxRepository,
    ChargebackEngine chargebackEngine,
    CostAnomalyRanker fallbackRanker,
    DataGovernancePolicy dataGovernancePolicy,
    TerraformTagScanner tagScanner,
    EventPublishingChannel eventChannel,
    WorkerHealthState healthState,
    IOptions<AnomalyWorkerOptions> options,
    ILogger<CostAnomalyWorker> logger) : BackgroundService
{
    private readonly PeriodicTimer timer = new(TimeSpan.FromMinutes(Math.Max(5, options.Value.PollIntervalMinutes)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Cloud cost anomaly worker started for billing scope {BillingScope}", options.Value.BillingScope);

        await RunCycle(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycle(stoppingToken);
        }
    }

    private async Task RunCycle(CancellationToken cancellationToken)
    {
        try
        {
            healthState.MarkStarted(DateTimeOffset.UtcNow);
            await RunOnce(cancellationToken);
            healthState.MarkSucceeded(DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            healthState.MarkFailed(DateTimeOffset.UtcNow, exception);
            logger.LogError(exception, "Cloud cost anomaly cycle failed; the worker will retry on the next interval.");
        }
    }

    private async Task RunOnce(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var since = started.AddDays(-options.Value.LookbackDays);

        logger.LogInformation("Polling Azure Cost Management from {Since:o} to {Until:o}", since, started);
        var records = await costClient.QueryDailyCostsAsync(since, started, cancellationToken);
        var allocations = chargebackEngine.Allocate(records);
        var governedRecords = dataGovernancePolicy.Normalize(records);
        var governedAllocations = dataGovernancePolicy.Normalize(allocations);

        await adxRepository.IngestCostsAsync(governedRecords, cancellationToken);
        await adxRepository.IngestChargebackAsync(governedAllocations, cancellationToken);

        var adxAnomalies = await adxRepository.QueryAnomaliesAsync(options.Value.AlertMinimumCostDelta, cancellationToken);
        var fallbackAnomalies = fallbackRanker.Rank(governedAllocations)
            .Where(x => x.ActualCost - x.ExpectedCost >= options.Value.AlertMinimumCostDelta)
            .ToArray();
        var anomalies = adxAnomalies.Concat(fallbackAnomalies).ToArray();

        var findings = tagScanner.Scan(options.Value.TerraformRoot);

        PublishFanOut(anomalies, governedAllocations, findings, started);

        logger.LogInformation(
            "Cycle complete. Records={RecordCount}, Allocations={AllocationCount}, Anomalies={AnomalyCount}, TagFindings={FindingCount}",
            governedRecords.Count,
            governedAllocations.Count,
            anomalies.Length,
            findings.Count);
    }

    private void PublishFanOut(
        IReadOnlyCollection<AnomalyScore> anomalies,
        IReadOnlyCollection<ChargebackAllocation> allocations,
        IReadOnlyCollection<TerraformTagFinding> findings,
        DateTimeOffset observedAt)
    {
        var dropped = 0;

        foreach (var anomaly in anomalies)
        {
            if (!eventChannel.TryPublish(EventEnvelopeFactory.FromAnomaly(anomaly)))
            {
                dropped++;
            }
        }

        foreach (var allocation in allocations)
        {
            if (!eventChannel.TryPublish(EventEnvelopeFactory.FromChargebackAllocation(allocation)))
            {
                dropped++;
            }
        }

        foreach (var finding in findings)
        {
            if (!eventChannel.TryPublish(EventEnvelopeFactory.FromOwnershipFinding(finding, observedAt)))
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            logger.LogWarning("Fan-out publishing channel rejected {Dropped} event(s) under backpressure this cycle", dropped);
        }
    }
}
