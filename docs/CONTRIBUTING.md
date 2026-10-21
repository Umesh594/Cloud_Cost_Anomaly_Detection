# Contributing

## Code review

- `CODEOWNERS` (repo root) names the required reviewer(s) per path. GitHub
  auto-requests their review on a PR touching those paths.
- Use `.github/PULL_REQUEST_TEMPLATE.md` — it is pre-filled for every new
  PR and includes a checklist that mirrors the required checks below.
- Every PR needs **at least one approving review from a code owner** before
  merge (branch protection intent below).

## Required status checks (branch protection intent)

This repo's CI is defined in `.github/workflows/ci.yml`. The following
jobs are intended to be configured as **required status checks** on the
`main` branch protection rule (GitHub Settings → Branches → Branch
protection rules → `main` → "Require status checks to pass before
merging"):

| Required check (job name in `ci.yml`) | What it enforces |
|---|---|
| `build-test-image` | `dotnet test` passes (19 unit tests across chargeback, anomaly ranking, data governance, Terraform tag scanning) and the worker's Docker image builds |
| `coverage-gate` | Line coverage does not regress below the threshold enforced in CI (currently 75%; measured baseline at the time this was wired up was ~80% line coverage on `CloudCost.Anomaly.Core`) |
| `terraform-validate` | `terraform fmt -check` and `terraform validate` pass for `infra/terraform` |
| `security-scan` | `dotnet list package --vulnerable` (NuGet) and `pip-audit` (`spark/requirements.txt`) report no new, unaddressed vulnerabilities |
| `required-checks` | A summary job that depends on all of the above and is the single check actually marked "required" in the branch protection UI — see note below |

**Why a summary job**: GitHub's "required status checks" list must name
jobs by their exact `name`, and renaming or splitting jobs later silently
breaks the protection rule until someone notices. `ci.yml` defines a final
`required-checks` job with `needs: [build-test-image, coverage-gate,
terraform-validate, security-scan]` so the branch protection rule only has
to require that one job — if any upstream job fails or is skipped, this
job fails too.

**This repo does not have admin access configured to set branch protection
rules from this change** (that is a GitHub repository settings change, not
a file in the repo) — the `required-checks` job exists in `ci.yml` so that
once a maintainer with repo admin rights adds it as a required check, the
enforcement is immediate and correct. Until that setting is applied, this
document is the record of intent, not yet an enforced gate.

## Running checks locally before opening a PR

```powershell
dotnet restore CloudCost.AnomalyDetection.sln
dotnet test CloudCost.AnomalyDetection.sln --configuration Release --collect:"XPlat Code Coverage"
dotnet list CloudCost.AnomalyDetection.sln package --vulnerable --include-transitive

cd infra/terraform
terraform fmt -check
terraform init -backend=false
terraform validate
cd ../../spark
docker build -t cloud-cost-spark-bench -f Dockerfile .
docker run --rm -v "${PWD}:/work" cloud-cost-spark-bench pip-audit -r requirements.txt
```

## Coverage threshold

The coverage gate parses the Cobertura report produced by `dotnet test
--collect:"XPlat Code Coverage"` using `dotnet-reportgenerator-globaltool`
and fails the build if line coverage drops below **75%**. This threshold
was set a few points below the measured baseline (~80%) at the time it was
introduced, intentionally, so normal test-writing variance doesn't trip
false failures — raise it over time as coverage improves, don't lower it to
make a failing PR pass.
