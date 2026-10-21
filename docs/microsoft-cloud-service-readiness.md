# Microsoft Cloud Service Readiness

This repository now includes a print-service reliability slice that maps the FinOps worker pattern to a Windows/cloud service domain.

## What Was Added

- C# `.NET 8` print-job telemetry records.
- Queue-health analyzer for failures, stuck jobs, p95 latency, payload size, and health state.
- Privacy-safe normalization for tenant, printer, queue, user, and document identifiers.
- ADX/KQL queue-health function for Azure Monitor or Application Insights investigation.
- xUnit tests covering degraded queues, healthy queues, and privacy redaction.

## Operational Flow

```text
Windows / connector telemetry
  -> privacy normalization
  -> queue health analyzer
  -> Azure Monitor / Application Insights / ADX
  -> live-site dashboard and investigation query
```

## Production Notes

- The code does not call Microsoft Universal Print APIs and does not claim real product integration.
- The same service lifecycle applies: implement, unit test, emit telemetry, investigate live issues, and maintain privacy-safe diagnostics.
- Application Insights export is configured through `Observability:ApplicationInsightsConnectionString`.
