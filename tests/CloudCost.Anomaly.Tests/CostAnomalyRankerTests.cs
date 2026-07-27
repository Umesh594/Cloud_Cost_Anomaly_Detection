using CloudCost.Anomaly.Core;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class CostAnomalyRankerTests
{
    [Fact]
    public void Rank_FindsLargePositiveSpike()
    {
        var start = new DateOnly(2026, 7, 1);
        var series = Enumerable.Range(0, 9)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", i == 8 ? 180 : 100, "USD", 0.99, "tag:team"))
            .ToArray();

        var anomalies = new CostAnomalyRanker().Rank(series);

        var anomaly = Assert.Single(anomalies);
        Assert.Equal("payments", anomaly.Team);
        Assert.True(anomaly.Severity >= 0.65);
    }

    [Fact]
    public void Rank_IgnoresNormalDailyVariation()
    {
        var start = new DateOnly(2026, 7, 1);
        var series = Enumerable.Range(0, 10)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", 100 + (i % 2), "USD", 0.99, "tag:team"))
            .ToArray();

        var anomalies = new CostAnomalyRanker().Rank(series);

        Assert.Empty(anomalies);
    }
}
