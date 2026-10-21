# On-Call Runbook: Cloud Cost Anomaly Detection & Chargeback

This runbook covers the components that actually exist in this repository:
the `.NET 8` worker (`src/CloudCost.Anomaly.Worker`), its Azure Cost
Management / Azure Data Explorer (ADX) integrations
(`src/CloudCost.Anomaly.Worker/Services`), the KQL anomaly/chargeback
queries (`kql/`), the Terraform IaC ownership scanner, and (new) the
Synapse-targeted PySpark batch job in `spark/`. It intentionally does not
reference components this project does not have (there is no API surface,
no message queue, no database beyond ADX).

## 1. How to detect something is wrong

There is no public HTTP endpoint in this service today. `Program.cs`
registers `AddHealthChecks().AddCheck<WorkerHealthCheck>("worker")`
(`src/CloudCost.Anomaly.Worker/Program.cs`), but the worker is built on
`Host.CreateApplicationBuilder` (a plain Generic Host), not a web host —
there is no Kestrel listener mapping `/healthz`, and `infra/k8s/deployment.yaml`
has no `livenessProbe`/`readinessProbe`. This is a known gap; until an HTTP
endpoint is wired up (tracked as follow-up work, not claimed as done here),
detect problems via:

1. **Structured logs** (Serilog → console → container logs → whatever log
   sink is attached, e.g. `kubectl logs deploy/cloud-cost-anomaly-worker`).
   `CostAnomalyWorker.RunCycle` logs `"Cycle complete..."` on success and
   `"Cloud cost anomaly cycle failed..."` on every exception
   (`src/CloudCost.Anomaly.Worker/CostAnomalyWorker.cs`). Absence of a
   `"Cycle complete"` log line for longer than `PollIntervalMinutes` (default
   60, see `appsettings.json`) is itself a signal, even without a probe.
2. **`WorkerHealthState`** (`src/CloudCost.Anomaly.Worker/WorkerHealthState.cs`)
   tracks `LastStartedAt` / `LastSucceededAt` / `LastFailedAt` / `LastError`
   in-process. `WorkerHealthCheck` reports `Unhealthy` when
   `now - LastSucceededAt > pollInterval + 10 minutes`. This logic is correct
   and tested implicitly by the worker's own cycle logging, but currently
   only observable through logs/metrics, not a liveness probe, until it is
   exposed over HTTP.
3. **ADX ingestion lag.** Since chargeback data lands in the `ChargebackDaily`
   table (`kql/schema.kql`), staleness in ADX is a direct proxy for worker
   health:
   ```kql
   ChargebackDaily
   | summarize LastIngested = max(Timestamp)
   | extend LagHours = datetime_diff('hour', now(), LastIngested)
   ```
   If `LagHours` exceeds roughly `PollIntervalMinutes/60 + 1`, the worker is
   not ingesting on schedule.
4. **Fan-out publishing silence.** Anomalies, chargeback allocations, and
   IaC ownership findings are routed through `EventPublishingService`
   (`src/CloudCost.Anomaly.Worker/Services/Publishing/EventPublishingService.cs`)
   to every sink listed in `EventPublishing__EnabledSinks`. A sink with no
   destination configured (e.g. `webhook` with an empty
   `EventPublishing__Webhook__Url`) logs at debug level and silently drops
   its events — check per-sink configuration, not just whether the worker
   cycle itself succeeded, before treating "no alerts" as "all healthy".
   Also check the `EventPublishingChannel.DroppedWhenFullCount` counter (via
   logs — `CostAnomalyWorker` logs a warning when its own `TryPublish` calls
   are rejected) for backpressure-driven event loss.
5. **AKS-level signals**: pod restarts/`CrashLoopBackOff`
   (`kubectl get pods`), and the `HorizontalPodAutoscaler` in
   `infra/k8s/deployment.yaml` sitting pinned at `maxReplicas` (5) under
   normal cost-polling load, which would indicate CPU exhaustion rather than
   organic scale.

## 2. Triage by failure mode

### A. Azure Cost Management API throttling / auth failure

Symptom: `CostManagementClient.QueryDailyCostsAsync`
(`src/CloudCost.Anomaly.Worker/Services/CostManagementClient.cs`) retries
via Polly on `408/429/5xx` (3 attempts, exponential backoff starting at 2s),
then throws. The cycle log will show
`"Cloud cost anomaly cycle failed; the worker will retry on the next interval."`
with the underlying `HttpRequestException` / `EnsureSuccessStatusCode`
message in `LastError`.

Steps:
1. Check `LastError` (from logs or `WorkerHealthState` if inspected via a
   debugger/diagnostic dump) for the HTTP status code.
2. `401/403` → token/identity problem. Confirm `AZURE_CLIENT_ID` /
   workload identity federation is correctly bound
   (`infra/k8s/deployment.yaml`'s `ServiceAccount` annotation
   `azure.workload.identity/client-id`) and that the identity has
   `Cost Management Reader` on the configured `AnomalyWorker__BillingScope`.
3. `429` → Cost Management API throttling. This is expected under heavy
   polling; the built-in retry handles transient cases. If it persists across
   multiple cycles, increase `AnomalyWorker__PollIntervalMinutes` (reduces
   call frequency) rather than retrying harder.
4. `BillingScope` empty → the client logs a warning and returns an empty
   result set (no exception, no chargeback output, no false alert). Verify
   `AnomalyWorker__BillingScope` is set in the Kubernetes secret
   (`cloud-cost-anomaly/billing-scope`).

Mitigation: the worker already retries and skips cleanly on misconfiguration
rather than crash-looping; no manual restart is required for transient
429/5xx — just confirm the *next* scheduled cycle recovers (check the ADX
ingestion-lag query above after one more `PollIntervalMinutes` interval).

### B. ADX ingestion failure

Symptom: `AdxCostRepository.IngestCostsAsync` /
`IngestChargebackAsync` (`src/CloudCost.Anomaly.Worker/Services/AdxCostRepository.cs`)
throw if the `.ingest inline` control command fails (bad cluster URI,
permission denied, malformed CSV from a tag value containing unexpected
characters, database/table not provisioned).

Steps:
1. Confirm `AnomalyWorker__AdxClusterUri` and `AnomalyWorker__AdxDatabase`
   resolve and that `kql/schema.kql` has been applied to that database (the
   README's Quick Start step is easy to skip on a fresh cluster).
2. Confirm the workload identity has `Database Ingestor` (and `Database
   Viewer` for the anomaly query) on the ADX database.
3. If a specific ingest fails with a parsing error, suspect a tag value
   containing an unescaped quote or delimiter; `AdxCostRepository.Csv()`
   escapes `"` but a tag value containing control characters or a `,`-heavy
   free-text field could still produce a malformed row. Check
   `DataGovernancePolicy.Normalize` (`src/CloudCost.Anomaly.Core/DataGovernancePolicy.cs`)
   output for the batch in question.
4. `.ingest inline` is synchronous and simple but not optimized for very
   large batches; if `LookbackDays` is increased significantly, consider
   queued ingestion instead (not implemented here — flag as a scaling
   limitation, don't claim it's handled).

Mitigation: because ingestion failure throws, the whole cycle is marked
failed and nothing partial is committed to ADX from that attempt (no
silent partial-ingest risk) — the next scheduled cycle will reprocess the
same lookback window since `since = started.AddDays(-LookbackDays)` is
relative to "now", not a saved cursor.

### C. Stale chargeback data (ADX anomaly detection stops firing)

Symptom: `AdxCostRepository.QueryAnomaliesAsync` always returns results
based on `ChargebackDaily` over the last 35 days
(`BuildAnomalyQuery`); if no new rows have landed, `series_decompose_anomalies`
has a flat/stale series and legitimately stops firing — this looks like "no
anomalies" but is actually "no data," which is worse.

Steps:
1. Run the ingestion-lag KQL query from section 1.3 first, before trusting
   an "all quiet" status.
2. Cross-check `kql/chargeback-report.kql` for the last 30 days — if
   `TotalCost` has not moved day over day, the pipeline is stalled, not
   quiet.
3. If stale, work backward through sections A and B above — staleness is
   always downstream of either a Cost Management polling failure or an ADX
   ingestion failure.

### D. Spark/Synapse batch job failure (`spark/jobs/*.py`)

The Spark jobs in this repo are a separate, offline batch path (see
`spark/README.md`) — they do not feed the near-real-time ADX anomaly
pipeline, so a Spark failure does not affect live alerting. It does affect
monthly/quarterly chargeback reconciliation and historical rollups.

Symptom: a Synapse Spark pool job run fails or times out.

Steps:
1. Check the Synapse Studio monitoring view (or, for this repo's local test
   harness, the container/driver stdout) for the failing stage. Both jobs
   print `rows/sec` throughput on success
   (`generate_synthetic_costs.py`, `aggregate_chargeback.py`) — absence of
   that final line means the job did not complete.
2. Common causes at larger scale than tested here: executor OOM (tune
   `spark.sql.shuffle.partitions` and executor memory — this repo's jobs
   default to 8 shuffle partitions, tuned for the benchmark sizes in
   `spark/benchmarks/results.json`, not for a production-size historical
   table), skewed partitions (one subscription or resource group dominating
   a partition), or a schema drift in the upstream Cost Management export
   that the aggregation job's `groupBy` columns no longer match.
3. Mitigation/rollback: these are idempotent batch jobs over partitioned
   Parquet output (`mode("overwrite")` per run) — simply re-run the job for
   the affected `usage_month` partition; there is no stateful cursor to
   repair.

## 3. Escalation path

1. **On-call engineer** (this repo's owning team, per `CODEOWNERS`):
   triage using sections 1–2 above; most Cost Management/ADX issues are
   config or permission problems resolvable without a code change.
2. **If a code fix is required**: open a PR following
   `docs/CONTRIBUTING.md` (required checks: unit tests, coverage, Terraform
   validate — see that doc and `.github/workflows/ci.yml`). Do not bypass
   required checks to hotfix; the worker's cycle-level error isolation means
   a failed cycle does not take down the whole deployment, so there is
   runway to fix properly.
3. **If the issue is Azure-side** (Cost Management API outage, ADX cluster
   outage): check the Azure Service Health dashboard for the subscription
   before assuming a code bug; the Polly retry policy and the
   per-cycle try/catch in `CostAnomalyWorker` are specifically designed so
   transient Azure-side issues self-heal on the next poll interval without
   paging anyone.
4. **If finance-facing chargeback numbers are wrong** (not just delayed):
   treat as higher severity than a pure availability issue — incorrect
   chargeback attribution has a business/financial impact beyond this
   system. Escalate to the FinOps/finance stakeholder alongside the
   engineering fix, and check `ChargebackEngine.ResolveTeam`'s confidence
   score (`src/CloudCost.Anomaly.Core/ChargebackEngine.cs`) and the
   Terraform tag scanner findings for the affected resources before
   assuming the bug is in the aggregation math rather than the ownership
   data.

## Honest limitations of this runbook

- No HTTP health endpoint exists yet; AKS liveness/readiness probes are not
  configured. This runbook relies on logs and ADX queries as a substitute,
  which is slower than a probe-based detection loop.
- No alerting-on-alerting exists: if every configured sink is unset or down,
  there is currently no secondary channel (e.g. PagerDuty) wired in this
  repo. Delivery retry is in-memory and per-process — a worker crash between
  a channel write and sink delivery loses that specific delivery attempt
  (not the underlying cost data, which is re-derived from Azure Cost
  Management on the next poll); there is no durable outbox yet.
- The Spark/Synapse batch path has not been deployed to a real Synapse
  workspace; failure modes in section 2.D beyond OOM/skew are inferred from
  general Spark operational experience, not from an incident that has
  actually occurred against this job in Synapse.
