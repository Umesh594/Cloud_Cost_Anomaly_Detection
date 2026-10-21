"""Generate a synthetic Azure cost-record dataset for the Spark/Synapse batch
ETL job to process.

The shape mirrors ``CostRecord`` in src/CloudCost.Anomaly.Core/CostRecord.cs
(one row per resource-level cost line item per day) so the Spark aggregation
in aggregate_chargeback.py produces output comparable to what
ChargebackEngine.Allocate() produces in the .NET worker.

Usage:
    python generate_synthetic_costs.py --rows 1000000 --out ../data/cost_records_1m

Design notes:
- Generation is done entirely with Spark SQL expressions (rand/pmod/
  element_at over broadcast arrays), not a Python UDF or a driver-side loop,
  so it scales the same way the aggregation job does and the row count is
  not limited by driver memory.
- Output is written as Parquet partitioned by `usage_month`, which keeps
  partition counts sane (a handful of months) while still demonstrating
  partition pruning for time-range queries, the same partitioning strategy
  a daily Cost Management pull would naturally produce.
"""
import argparse
import time

from pyspark.sql import SparkSession
from pyspark.sql import functions as F

SUBSCRIPTIONS = [f"sub-{i:04d}" for i in range(1, 51)]  # 50 subscriptions
RESOURCE_GROUPS = [f"rg-{i:04d}" for i in range(1, 201)]  # 200 resource groups
SERVICES = [
    "Virtual Machines", "Storage", "SQL Database", "App Service",
    "Azure Kubernetes Service", "Azure Data Explorer", "Synapse Analytics",
    "Cosmos DB", "Functions", "Log Analytics", "Application Gateway",
    "Key Vault", "Container Registry", "Event Hubs", "Data Factory",
]
METER_CATEGORIES = [
    "Compute", "Storage", "Networking", "Databases", "Analytics", "Management",
]
TEAMS = [
    "payments", "search", "identity", "data-platform", "growth", "sre",
    "mobile", "checkout", "ml-platform", "unallocated",
]
CURRENCIES = ["USD", "USD", "USD", "USD", "EUR", "GBP"]  # weighted toward USD


def build_dataset(spark: SparkSession, rows: int, seed: int = 42):
    df = spark.range(0, rows).withColumnRenamed("id", "row_id")

    def pick(colname: str, values: list, salt: int):
        arr = F.array(*[F.lit(v) for v in values])
        idx = F.pmod((F.col("row_id") * F.lit(31 + salt) + F.lit(seed)), F.lit(len(values)))
        return F.element_at(arr, idx.cast("int") + 1).alias(colname)

    df = df.select(
        "row_id",
        pick("subscription_id", SUBSCRIPTIONS, 1),
        pick("resource_group", RESOURCE_GROUPS, 2),
        pick("service_name", SERVICES, 3),
        pick("meter_category", METER_CATEGORIES, 4),
        pick("team_tag", TEAMS, 5),
        pick("currency", CURRENCIES, 6),
        # 180-day lookback window, one row's date derived deterministically
        # from row_id so re-runs are reproducible.
        F.date_sub(F.current_date(), (F.pmod(F.col("row_id") * 7 + seed, F.lit(180))).cast("int")).alias("usage_date"),
        # Log-normal-ish cost distribution with an occasional spike (~0.5%
        # of rows) to emulate real spend skew (a few resources dominate
        # spend; most are small).
        F.when(
            F.pmod(F.col("row_id") * 97 + seed, F.lit(200)) == 0,
            (F.rand(seed) * F.lit(4000) + F.lit(500)),
        ).otherwise(
            (F.rand(seed + 1) * F.lit(45) + F.lit(0.5))
        ).alias("pretax_cost"),
    )

    df = df.withColumn(
        "resource_id",
        F.concat(
            F.lit("/subscriptions/"), F.col("subscription_id"),
            F.lit("/resourceGroups/"), F.col("resource_group"),
            F.lit("/providers/Microsoft.Compute/"), F.col("service_name"),
            F.lit("/"), F.col("row_id"),
        ),
    ).withColumn("pretax_cost", F.round(F.col("pretax_cost"), 4))

    df = df.withColumn("usage_month", F.date_format(F.col("usage_date"), "yyyy-MM"))

    return df.select(
        "usage_date", "usage_month", "subscription_id", "resource_id",
        "resource_group", "meter_category", "service_name", "team_tag",
        "currency", "pretax_cost",
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rows", type=int, required=True)
    parser.add_argument("--out", type=str, required=True)
    parser.add_argument("--master", type=str, default="local[*]")
    args = parser.parse_args()

    spark = (
        SparkSession.builder
        .master(args.master)
        .appName(f"generate-synthetic-costs-{args.rows}")
        .config("spark.sql.shuffle.partitions", "8")
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("WARN")

    start = time.time()
    df = build_dataset(spark, args.rows)
    (
        df.repartition(8)
        .write.mode("overwrite")
        .partitionBy("usage_month")
        .parquet(args.out)
    )
    elapsed = time.time() - start
    print(f"Wrote {args.rows} rows to {args.out} in {elapsed:.2f}s "
          f"({args.rows / elapsed:,.0f} rows/sec)")
    spark.stop()


if __name__ == "__main__":
    main()
