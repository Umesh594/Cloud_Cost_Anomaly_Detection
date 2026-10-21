# Local test/benchmark harness for the Synapse-targeted PySpark jobs in
# spark/jobs. This is NOT the production deployment path (production is an
# Azure Synapse Spark pool, see spark/README.md) -- it exists so the batch
# ETL jobs can be built and benchmarked with a real Spark session without
# depending on the host OS (avoids the Windows winutils/Hadoop native-IO
# dependency entirely by running Linux + a real JDK, matching what Synapse
# Spark pools run on).
FROM python:3.11-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends openjdk-21-jre-headless procps \
    && rm -rf /var/lib/apt/lists/*

ENV JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64
RUN pip install --no-cache-dir pyspark==3.5.8 pandas==2.2.3 numpy==2.1.3 pip-audit==2.7.3

WORKDIR /work
