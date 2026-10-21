"""Benchmark harness: generates synthetic cost-record datasets at several
sizes, times the Spark generation + aggregation jobs, and (for sizes where
it's feasible) times a naive single-threaded pandas equivalent for
comparison.

Run inside the spark/Dockerfile container (Linux + JDK) so the comparison is
apples-to-apples with a real Spark execution and is not affected by the
Windows winutils/Hadoop native-IO limitation documented in spark/README.md.

Usage (from the spark/ directory):
    docker build -t cloud-cost-spark-bench -f Dockerfile .
    docker run --rm -v "$PWD:/work" cloud-cost-spark-bench python benchmarks/run_benchmark.py
"""
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESULTS_PATH = ROOT / "benchmarks" / "results.json"

SIZES = [100_000, 1_000_000, 5_000_000]
PANDAS_SIZES = [100_000, 1_000_000]  # 5M rows single-threaded is excluded; see README


def run(cmd):
    print(f"$ {' '.join(cmd)}", flush=True)
    start = time.time()
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)
    elapsed = time.time() - start
    print(proc.stdout[-2000:])
    if proc.returncode != 0:
        print(proc.stderr[-4000:], file=sys.stderr)
        raise RuntimeError(f"command failed: {' '.join(cmd)}")
    return elapsed, proc.stdout


def pandas_naive_benchmark(rows: int):
    """Single-threaded pandas equivalent of aggregate_chargeback.py's daily
    rollup, generating the same shape of data in-process (no Spark) and
    timing only the aggregation step so it is a fair comparison against the
    Spark aggregation job, not against Spark's (parallel) data generation."""
    import numpy as np
    import pandas as pd

    rng = np.random.default_rng(42)
    subs = [f"sub-{i:04d}" for i in range(1, 51)]
    services = [
        "Virtual Machines", "Storage", "SQL Database", "App Service",
        "Azure Kubernetes Service", "Azure Data Explorer", "Synapse Analytics",
        "Cosmos DB", "Functions", "Log Analytics",
    ]
    teams = [
        "payments", "search", "identity", "data-platform", "growth", "sre",
        "mobile", "checkout", "ml-platform", "unallocated",
    ]
    currencies = ["USD"] * 4 + ["EUR", "GBP"]

    df = pd.DataFrame({
        "usage_date": rng.integers(0, 180, size=rows),
        "subscription_id": rng.choice(subs, size=rows),
        "service_name": rng.choice(services, size=rows),
        "team_tag": rng.choice(teams, size=rows),
        "currency": rng.choice(currencies, size=rows),
        "pretax_cost": rng.lognormal(mean=2.0, sigma=1.2, size=rows),
    })

    start = time.time()
    grouped = (
        df.groupby(["usage_date", "subscription_id", "service_name", "team_tag", "currency"])
        ["pretax_cost"].agg(total_cost="sum", record_count="count")
        .reset_index()
    )
    top_teams = (
        df.groupby(["team_tag", "currency"])["pretax_cost"].sum()
        .reset_index().sort_values("pretax_cost", ascending=False)
    )
    elapsed = time.time() - start
    return elapsed, len(grouped), len(top_teams)


def main():
    results = {"spark": {}, "pandas_naive": {}}

    for rows in SIZES:
        data_path = f"data/bench_{rows}"
        out_path = f"data/bench_{rows}_agg"

        gen_elapsed, gen_out = run([
            "python", "jobs/generate_synthetic_costs.py",
            "--rows", str(rows), "--out", data_path,
        ])
        agg_elapsed, agg_out = run([
            "python", "jobs/aggregate_chargeback.py",
            "--in", data_path, "--out", out_path,
        ])

        results["spark"][rows] = {
            "generate_seconds": round(gen_elapsed, 2),
            "generate_rows_per_sec": round(rows / gen_elapsed, 1),
            "aggregate_seconds": round(agg_elapsed, 2),
            "aggregate_rows_per_sec": round(rows / agg_elapsed, 1),
        }

    for rows in PANDAS_SIZES:
        elapsed, group_count, team_count = pandas_naive_benchmark(rows)
        results["pandas_naive"][rows] = {
            "aggregate_seconds": round(elapsed, 3),
            "aggregate_rows_per_sec": round(rows / elapsed, 1),
            "group_count": group_count,
            "team_count": team_count,
        }

    RESULTS_PATH.write_text(json.dumps(results, indent=2))
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
