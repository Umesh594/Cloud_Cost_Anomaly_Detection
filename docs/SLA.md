# Service Level Objectives

These are *target* objectives for a system of this shape (single-tenant
FinOps batch/near-real-time worker), written against what the code actually
does today. This project has not been operated in production, so these are
design targets, not measured SLAs from live incident history.

## Scope

- **In scope**: the `.NET 8` worker's poll → chargeback-allocate → ADX-ingest
  → anomaly-query → alert-publish cycle; the Terraform IaC ownership scan;
  the Spark/Synapse batch chargeback rollup.
- **Out of scope**: Azure Cost Management API availability, Azure Data
  Explorer cluster availability, AKS control-plane availability, and Azure
  Synapse workspace availability — these are upstream Azure platform SLAs
  this system depends on but does not control.

## Objectives

| Dimension | Target | Basis |
|---|---|---|
| Chargeback data freshness | Chargeback data in ADX no more than `PollIntervalMinutes + 10 min` stale | `WorkerHealthCheck`'s own staleness threshold (`pollInterval + TimeSpan.FromMinutes(10)`), `src/CloudCost.Anomaly.Worker/WorkerHealthCheck.cs` |
| Anomaly alert latency | Anomaly surfaced within one poll cycle (default 60 min) of the underlying spend anomaly landing in ADX | `CostAnomalyWorker` runs `QueryAnomaliesAsync` every cycle immediately after ingestion; there is no separate alerting delay built in |
| Cycle success rate | No hard target set in code; `AlertMinimumCostDelta` and retry policy are tuned to avoid false pages, not to guarantee a numeric success rate | `CostManagementClient`'s Polly retry (3 attempts, exponential backoff) absorbs transient Cost Management failures without failing the cycle |
| Terraform ownership scan | Runs once per cycle against `TerraformRoot`; findings are only as fresh as the last `git`-synced Terraform state the worker's filesystem sees | `TerraformTagScanner.Scan`, `src/CloudCost.Anomaly.Core/TerraformTagScanner.cs` |
| Spark batch rollup | Best-effort; intended as a monthly/quarterly job, not a latency-sensitive path (see `spark/README.md` "two-speed architecture") | Design intent, not a measured production SLA |

## Error budget framing

Because `CostAnomalyWorker.RunCycle` catches all non-cancellation exceptions
per cycle and schedules the next cycle regardless, a single Azure-side
transient failure consumes exactly one cycle's worth of freshness (at most
`PollIntervalMinutes`), not a cascading outage. The practical error budget
question is: **how many consecutive failed cycles are tolerable before the
chargeback data is considered unusable for finance reporting?** This repo
does not currently define that threshold in config; `WorkerHealthCheck`'s
`Unhealthy` threshold (one missed cycle + 10 minutes) is a reasonable
starting point for a paging threshold, but has not been validated against
real finance reporting cadences.

## What would make these real, measured SLAs

1. Expose `WorkerHealthState` over HTTP (see `docs/RUNBOOK.md` limitations)
   and feed it into an actual uptime/alerting tool, so freshness and success
   rate are measured, not just checked on demand.
2. Run the system against real Azure Cost Management / ADX endpoints for at
   least one full reporting cycle (month-end close) before committing to a
   numeric SLA finance would rely on.
3. Track the Spark job's actual run history once it is scheduled against a
   real Synapse Spark pool (see `spark/README.md`) — the benchmark numbers in
   `spark/benchmarks/results.json` are from a local/containerized test run,
   not from Synapse, and Synapse job start-up/queuing time is not reflected
   in them.
