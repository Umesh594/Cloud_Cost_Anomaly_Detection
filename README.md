# Cloud Cost Anomaly Detection and Chargeback System

Production-oriented FinOps backend for Azure-heavy organizations. The worker continuously pulls Azure Cost Management data, writes raw and chargeback-ready facts into Azure Data Explorer, detects spend spikes with KQL time-series decomposition, and scans infrastructure-as-code for ownership tags so finance can allocate costs without manual spreadsheet cleanup.

## Architecture

- **.NET 8 worker** polls Azure Cost Management and runs chargeback attribution.
- **Azure Data Explorer/KQL** stores daily facts and runs `series_decompose_anomalies` for seasonal anomaly detection.
- **Policy-as-code ownership engine** maps resources to teams from tags, subscription ownership, resource group naming, and Terraform scan signals.
- **AKS + workload identity** runs the worker without secrets.
- **Terraform** provisions AKS, ADX, managed identity, and RBAC.
- **Docker + GitHub Actions** provide repeatable build, test, image, and Terraform validation paths.

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
kubectl create secret generic cloud-cost-anomaly --from-literal=billing-scope='subscriptions/<subscription-id>'
kubectl apply -f infra/k8s/deployment.yaml
```

## Interview Talking Points

- Uses Azure-native `series_decompose_anomalies` instead of hand-rolled statistics for seasonality-aware detection.
- Keeps a local robust MAD-based ranker for fallback scoring and explanation.
- Demonstrates distributed systems fundamentals: idempotent polling windows, retries, structured logging, OpenTelemetry, health checks, horizontal scaling, and managed identity.
- Shows FinOps product thinking: chargeback confidence, ownership attribution source, ADX report queries, and IaC governance.

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

## Important Configuration

Set these as environment variables, Kubernetes config, or `appsettings` overrides:

- `AnomalyWorker__BillingScope`: Cost Management scope such as `subscriptions/<id>`.
- `AnomalyWorker__AdxClusterUri`: ADX cluster URI.
- `AnomalyWorker__AdxDatabase`: defaults to `CloudCost`.
- `AnomalyWorker__AlertWebhookUrl`: optional webhook for alert payloads.
- `AZURE_CLIENT_ID`: workload identity or service principal client id for local container runs.

## Repository Layout

- `src/CloudCost.Anomaly.Core`: chargeback, anomaly ranking, and IaC ownership scanner.
- `src/CloudCost.Anomaly.Worker`: background worker and Azure integrations.
- `kql`: ADX schema, anomaly function, chargeback report, and root-cause query.
- `infra/terraform`: Azure infrastructure.
- `infra/k8s`: AKS deployment manifests.
- `tests`: unit tests for chargeback and anomaly logic.
