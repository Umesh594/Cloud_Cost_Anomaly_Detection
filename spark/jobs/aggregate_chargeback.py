"""Batch chargeback aggregation job intended to run on an Azure Synapse
Spark pool.

This is the Spark-scale counterpart to ChargebackEngine.Allocate() in
src/CloudCost.Anomaly.Core/ChargebackEngine.cs. The .NET engine does
confidence-scored per-record team resolution (tag lookup -> policy
subscription/resource-group/service-hint fallback) and is well suited to
the near-real-time per-cycle volume the worker pulls from Azure Cost
Management. This job assumes team attribution has already happened upstream
(via the tag column written at ingestion, or a prior enrichment pass) and
focuses on what Spark is actually good at: rolling up hundreds of millions of
historical cost-record rows into:

  1. daily chargeback summaries by (date, subscription, service, team) --
     the same grouping ChargebackEngine.Allocate() produces, at Spark scale.
  2. a top-spenders report per subscription and per team, for finance
     drill-down and anomaly triage context.

Why Synapse Spark pools in production (see also README.md "Spark + ADX:
a two-speed architecture"):
  - The .NET worker's poll/allocate/write cycle is designed for the
    rolling lookback window (LookbackDays, default small) it needs for
    near-real-time anomaly alerts -- it is not meant to scan a full
    historical cost estate.
  - Monthly/quarterly chargeback reconciliation, cost-center audits, and
    backfills after a tagging policy change need to re-aggregate the full
    historical fact table (potentially hundreds of millions of rows across
    all subscriptions). That's a batch, horizontally-scalable job, which is
    exactly what a Synapse Spark pool (autoscaling, pay-per-job, same
    workspace as the rest of an Azure data estate) is for.
  - Output of this job (parquet, or written to an ADX "cold" table via the
    Kusto Spark connector) feeds Power BI / finance exports; it does not
    need to be on the sub-minute latency path the ADX anomaly detection
    pipeline uses.

Usage:
    python aggregate_chargeback.py --in ../data/cost_records_1m --out ../data/chargeback_1m
"""
import argparse
import time

from pyspark.sql import SparkSession
from pyspark.sql import functions as F
from pyspark.sql.window import Window


def aggregate(df):
    daily = (
        df.groupBy("usage_date", "subscription_id", "service_name", "team_tag", "currency")
        .agg(
            F.sum("pretax_cost").alias("total_cost"),
            F.count("*").alias("record_count"),
        )
        .withColumnRenamed("team_tag", "team")
    )
    return daily


def top_spenders(daily_df, by_col: str, top_n: int = 10):
    totals = daily_df.groupBy(by_col, "currency").agg(F.sum("total_cost").alias("total_cost"))
    window = Window.partitionBy("currency").orderBy(F.col("total_cost").desc())
    ranked = totals.withColumn("rank", F.row_number().over(window))
    return ranked.filter(F.col("rank") <= top_n).orderBy("currency", "rank")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--in", dest="inp", type=str, required=True)
    parser.add_argument("--out", type=str, required=True)
    parser.add_argument("--master", type=str, default="local[*]")
    args = parser.parse_args()

    spark = (
        SparkSession.builder
        .master(args.master)
        .appName("aggregate-chargeback")
        .config("spark.sql.shuffle.partitions", "8")
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("WARN")

    start = time.time()
    df = spark.read.parquet(args.inp)
    row_count = df.count()

    daily = aggregate(df)
    daily.write.mode("overwrite").partitionBy("usage_date").parquet(f"{args.out}/daily_chargeback")

    top_by_sub = top_spenders(daily, "subscription_id")
    top_by_sub.write.mode("overwrite").parquet(f"{args.out}/top_subscriptions")

    top_by_team = top_spenders(daily, "team")
    top_by_team.write.mode("overwrite").parquet(f"{args.out}/top_teams")

    elapsed = time.time() - start
    print(f"Aggregated {row_count} input rows from {args.inp} in {elapsed:.2f}s "
          f"({row_count / elapsed:,.0f} rows/sec)")

    spark.stop()


if __name__ == "__main__":
    main()
