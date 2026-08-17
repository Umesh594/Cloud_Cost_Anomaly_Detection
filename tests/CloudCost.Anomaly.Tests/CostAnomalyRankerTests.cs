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
        Assert.Contains("Ensemble anomaly", anomaly.Explanation);
        Assert.Contains("robust_z", anomaly.Explanation);
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

    [Fact]
    public void Rank_UsesBusinessImpactToPrioritizeExpensivePersistentSpikes()
    {
        var start = new DateOnly(2026, 7, 1);
        var smallSpike = Enumerable.Range(0, 10)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "analytics", "ADX", "sub-a", i >= 8 ? 35 : 20, "USD", 0.92, "tag:team"));
        var largeSpike = Enumerable.Range(0, 10)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "ai-platform", "Azure OpenAI", "sub-a", i >= 8 ? 950 : 500, "USD", 0.97, "tag:team"));

        var anomalies = new CostAnomalyRanker().Rank(smallSpike.Concat(largeSpike)).ToArray();

        Assert.NotEmpty(anomalies);
        Assert.Equal("ai-platform", anomalies[0].Team);
        Assert.Contains("impact=$", anomalies[0].Explanation);
    }
}
