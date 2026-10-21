# Cloud Cost Anomaly Detection and Chargeback System

Production-oriented FinOps backend for Azure-heavy organizations. The worker continuously pulls Azure Cost Management data, writes raw and chargeback-ready facts into Azure Data Explorer, detects spend spikes with KQL time-series decomposition, and scans infrastructure-as-code for ownership tags so finance can allocate costs without manual spreadsheet cleanup.

## Architecture

- **.NET 8 worker** polls Azure Cost Management and runs chargeback attribution.
- **Azure Data Explorer/KQL** stores daily facts and runs `series_decompose_anomalies` for seasonal anomaly detection.
- **Policy-as-code ownership engine** maps resources to teams from tags, subscription ownership, resource group naming, and Terraform scan signals.
- **AKS + workload identity** runs the worker without secrets.
- **PySpark batch ETL** (`spark/`), targeted at an Azure Synapse Spark pool, re-aggregates full historical cost data for month-end/quarterly chargeback reconciliation — a slower, horizontally-scaled complement to the ADX/.NET near-real-time path. See `spark/README.md` for the two-speed architecture rationale and real local benchmark numbers.
- **Terraform** provisions AKS, ADX, managed identity, and RBAC.
- **Docker + GitHub Actions** provide repeatable build, test, image, and Terraform validation paths.
- **Fan-out publishing pipeline** routes anomaly, chargeback, and IaC-ownership events to multiple
  independent destinations (ADX archival table, webhook, file/blob archive) through a pluggable
  `IEventSink` abstraction with schema-compatibility checks and idempotent, at-least-once delivery. See
  [Publishing pipeline](#publishing-pipeline).

## Business Outcome

Finance gets a daily chargeback report grouped by team, service, subscription, and currency. Engineering teams get alerts when spend jumps beyond the learned ADX baseline, including the likely service and ownership source. Untagged resources are caught at IaC review time and still receive a confidence-scored inferred owner.

## Quick Start

```powershell
docker compose build
docker compose up
```

For Azure deployment:

```powershell
cd infra/terraform
terraform init
terraform apply -var "billing_scope_resource_id=/subscriptions/<subscription-id>"
```

Apply ADX schema:

```powershell
# Run the commands in kql/schema.kql against the CloudCost ADX database.
```

Deploy to AKS:

```powershell
kubectl apply -f infra/k8s/configmap.yaml
kubectl create secret generic cloud-cost-anomaly `
  --from-literal=billing-scope='subscriptions/<subscription-id>' `
  --from-literal=application-insights-connection-string='<connection-string>'
kubectl apply -f infra/k8s/deployment.yaml
```

## Interview Talking Points

- Uses Azure-native `series_decompose_anomalies` instead of hand-rolled statistics for seasonality-aware detection.
- Keeps a local robust MAD-based ranker for fallback scoring and explanation.
- Demonstrates distributed systems fundamentals: idempotent polling windows, retries, structured logging, OpenTelemetry, health checks, horizontal scaling, and managed identity.
- Shows FinOps product thinking: chargeback confidence, ownership attribution source, ADX report queries, and IaC governance.
- Adds a Microsoft-style cloud service reliability slice for print-job telemetry: privacy-safe C# queue analysis, p95 latency, stuck-job detection, and ADX/App Insights investigation queries.

## Beyond FinOps: the anomaly-detection core is domain-general

`series_decompose_anomalies()` and the local `CostAnomalyRanker` fallback
have nothing FinOps-specific in their math — both are seasonal time-series
decomposition over a numeric value grouped by dimensions. `kql/device-
telemetry-anomalies.kql` applies the identical technique to device/hardware
telemetry (CPU%, battery drain, packet-drop rate) instead of daily spend,
shaped to match what a native ETW provider would export at scale — see the
`chrono_feed` project's `ProcessTelemetry` event for a concrete producer of
that shape. Same pipeline pattern, different signal: this is the piece that
maps the "telemetry and trace data ... at scale" side of a device-
fundamentals role onto something this repo already does well for cost data.

## Publishing pipeline

Anomalies, chargeback allocations, and IaC ownership findings are converted into schema-versioned
`SinkEvent` envelopes (`src/CloudCost.Anomaly.Core/Publishing`) and handed to a bounded
`System.Threading.Channels.Channel` (`EventPublishingChannel`) rather than written directly to a single
destination. A background dispatcher (`EventPublishingService`) drains that channel and fans each event
out to every *active* `IEventSink`:

- `AdxEventSink` ("adx") — archives the full event, including fields a given reader may not understand
  yet, into a dedicated ADX table (`EventPublishing:Adx:Table`, independent of the raw cost/chargeback
  ingestion tables used for anomaly detection itself).
- `WebhookEventSink` ("webhook") — HTTP POSTs the event JSON to `EventPublishing:Webhook:Url`.
- `FileArchiveEventSink` ("archive") — appends newline-delimited JSON per event type/day under
  `EventPublishing:Archive:Directory` (a local path in dev, a mounted Azure Files/Blob volume in AKS).

What makes this more than three if-branches:

- **Dynamic onboarding.** Which sinks are active is entirely config-driven
  (`EventPublishing:EnabledSinks`) via `EventSinkRegistry`; a new destination is onboarded by adding an
  `IEventSink` implementation and registering it in DI — no change to `EventPublishingService` or to
  `CostAnomalyWorker`.
- **Schema compatibility, not just a version number.** `EventSchemaCatalog` tracks each field's
  introduced/removed version per event type. `EventSchema.CheckCompatibility` rejects a delivery when a
  sink pinned to an older version is missing a field that was required as of that version (see the
  `chargeback.allocation` v1→v2 field rename in `EventSchemaCatalog`), and `ProjectToVersion` strips
  fields a sink doesn't understand instead of handing it fields it can't interpret.
- **At-least-once delivery with idempotency.** `IdempotencyStore` is a lock-free, TTL-bound dedup table
  keyed by `(sink name, event id)`; the event id is a deterministic hash of business identity (not a
  random GUID), so a worker-cycle retry that recomputes the same anomaly still dedupes correctly per sink.
- **Backpressure.** The channel is bounded (`EventPublishing:ChannelCapacity`) with a documented,
  configurable overflow policy (`DropOldest` / `DropNewest` / `Wait`) rather than growing without limit
  when sinks fall behind ingestion.
- **Isolation.** Each `(event, sink)` delivery is retried independently (Polly, exponential backoff); one
  sink erroring repeatedly never blocks or fails delivery to any other sink.

Tests for this layer live in `tests/CloudCost.Anomaly.Tests/EventSchemaCompatibilityTests.cs`,
`IdempotencyStoreTests.cs`, `EventEnvelopeFactoryTests.cs`, `EventSinkRegistryTests.cs`, and
`EventPublishingServiceTests.cs`.

Current limitation, stated plainly: retry is in-memory and per-process. If the worker crashes mid-cycle
after a channel write but before a sink delivery completes, that specific delivery attempt is lost (the
underlying cost/chargeback data itself is not, since it is re-derived from Azure Cost Management on the
next poll). A durable outbox (e.g. an ADX or storage-queue-backed retry log) would be the next step to
make delivery durable across process restarts, not just within one.

## Important Configuration

Set these as environment variables, Kubernetes config, or `appsettings` overrides:

- `AnomalyWorker__BillingScope`: Cost Management scope such as `subscriptions/<id>`.
- `AnomalyWorker__AdxClusterUri`: ADX cluster URI.
- `AnomalyWorker__AdxDatabase`: defaults to `CloudCost`.
- `AZURE_CLIENT_ID`: workload identity or service principal client id for local container runs.
- `EventPublishing__EnabledSinks`: which fan-out sinks are active (any of `adx`, `webhook`, `archive`); a
  sink that is registered in DI but not listed here never receives events.
- `EventPublishing__Webhook__Url`: optional webhook endpoint for the `webhook` sink's alert payloads.
- `EventPublishing__Archive__Directory`: filesystem (or mounted blob/file-share) path the `archive` sink
  writes newline-delimited JSON to.
- `EventPublishing__Adx__Table`: ADX table the `adx` sink archives published events into (separate from
  the raw cost/chargeback ingestion tables), defaults to `PublishedEvents`.

See [Publishing pipeline](#publishing-pipeline) below for how these fit together.

## Repository Layout

- `src/CloudCost.Anomaly.Core`: chargeback, anomaly ranking, and IaC ownership scanner.
- `src/CloudCost.Anomaly.Worker`: background worker and Azure integrations.
- `kql`: ADX schema, anomaly functions, chargeback reports, print-service telemetry, and root-cause queries.
- `infra/terraform`: Azure infrastructure.
- `infra/k8s`: AKS deployment manifests.
- `tests`: unit tests for chargeback and anomaly logic.
- `spark`: PySpark batch ETL targeted at Azure Synapse Spark pools, plus its local Docker-based test/benchmark harness.
- `docs/RUNBOOK.md`, `docs/SLA.md`: on-call triage steps and service level objectives.
- `docs/CONTRIBUTING.md`: required checks and review process for PRs.
