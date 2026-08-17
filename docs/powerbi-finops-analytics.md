# Power BI FinOps Analytics

This project includes a Power BI-ready semantic layer for executive cloud-cost analysis.

## Dataset

Run `kql/powerbi-executive-dataset.kql` in Azure Data Explorer. Power BI should connect to:

```kql
PowerBI_FinOpsExecutiveDataset(90)
```

The dataset exposes daily cost, expected baseline cost, anomaly severity, avoidable spend, ownership confidence, team, service, month, and currency. The grain is `Date + Team + ServiceName + Currency`.

## Report Pages

1. Executive cost overview: total cost, MTD cost, variance %, avoidable spend, anomaly count.
2. Anomaly operations: team/service heatmap by severity, avoidable spend waterfall, anomaly explanation table.
3. Chargeback governance: team allocation, attribution confidence, unallocated/low-confidence spend.
4. Service root cause: service trend, expected-vs-actual cost, subscription contribution.

## Business Value

The BI model converts raw Azure spend into operational decisions: finance sees chargeback coverage, engineering sees cost spikes ranked by severity and avoidable spend, and platform teams can prioritize ownership-tag fixes by attribution confidence.
