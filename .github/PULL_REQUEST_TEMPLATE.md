## What changed and why

<!-- Summarize the change. Link an issue if one exists. -->

## Area(s) touched

- [ ] `src/CloudCost.Anomaly.Core` (chargeback, anomaly ranking, IaC ownership)
- [ ] `src/CloudCost.Anomaly.Worker` (Azure Cost Management / ADX / alerting)
- [ ] `spark/` (Synapse-targeted batch ETL)
- [ ] `infra/terraform` or `infra/k8s`
- [ ] `kql/`
- [ ] `.github/` (CI/CD)
- [ ] Docs only

## Checklist (required before merge — see docs/CONTRIBUTING.md)

- [ ] `dotnet test` passes locally (`dotnet test CloudCost.AnomalyDetection.sln`)
- [ ] New/changed logic has unit test coverage; coverage has not regressed
      below the threshold enforced in CI
- [ ] `terraform fmt -check` and `terraform validate` pass if `infra/terraform`
      changed
- [ ] Dependency vulnerability scan (`dotnet list package --vulnerable`,
      `pip-audit` for `spark/`) shows no new, unaddressed findings
- [ ] If this changes the worker's failure/alerting behavior, `docs/RUNBOOK.md`
      is updated to match
- [ ] If this changes Spark job behavior, `spark/README.md` and
      `spark/benchmarks/results.json` assumptions are still accurate (re-run
      the benchmark if the change affects performance-relevant code paths)

## Test plan

<!-- How did you verify this change? Include commands run and their output,
     not just "tested locally". -->

## Rollback plan

<!-- If this ships a regression, what's the fastest safe way back? e.g.
     revert this PR, roll back the AKS deployment image tag, etc. -->
