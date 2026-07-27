using CloudCost.Anomaly.Core;
using CloudCost.Anomaly.Worker.Services;
using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker;

public sealed class CostAnomalyWorker(
    ICostManagementClient costClient,
    IAdxCostRepository adxRepository,
    ChargebackEngine chargebackEngine,
    CostAnomalyRanker fallbackRanker,
    TerraformTagScanner tagScanner,
    IAlertPublisher alertPublisher,
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

        await adxRepository.IngestCostsAsync(records, cancellationToken);
        await adxRepository.IngestChargebackAsync(allocations, cancellationToken);

        var adxAnomalies = await adxRepository.QueryAnomaliesAsync(options.Value.AlertMinimumCostDelta, cancellationToken);
        var fallbackAnomalies = fallbackRanker.Rank(allocations)
            .Where(x => x.ActualCost - x.ExpectedCost >= options.Value.AlertMinimumCostDelta)
            .ToArray();

        var findings = tagScanner.Scan(options.Value.TerraformRoot);
        await alertPublisher.PublishAsync(adxAnomalies.Concat(fallbackAnomalies), findings, cancellationToken);

        logger.LogInformation(
            "Cycle complete. Records={RecordCount}, Allocations={AllocationCount}, Anomalies={AnomalyCount}, TagFindings={FindingCount}",
            records.Count,
            allocations.Count,
            adxAnomalies.Count + fallbackAnomalies.Length,
            findings.Count);
    }
}
