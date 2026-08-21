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

    [Fact]
    public void Rank_ReturnsEmpty_WhenSeriesShorterThanLookbackWindow()
    {
        // LookbackDays is 7, so a series with fewer than 8 points has no
        // baseline to compare against and must not throw or false-positive.
        var start = new DateOnly(2026, 7, 1);
        var series = Enumerable.Range(0, 5)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", 100 + i, "USD", 0.99, "tag:team"))
            .ToArray();

        var anomalies = new CostAnomalyRanker().Rank(series);

        Assert.Empty(anomalies);
    }

    [Fact]
    public void Rank_NeverFlagsACostDecrease()
    {
        // A day that drops well below the baseline is not an "anomaly" in
        // the FinOps sense this ranker exists for -- the ranker explicitly
        // gates on delta > 0, and this pins that gate against regression.
        var start = new DateOnly(2026, 7, 1);
        var series = Enumerable.Range(0, 9)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", i == 8 ? 20 : 100, "USD", 0.99, "tag:team"))
            .ToArray();

        var anomalies = new CostAnomalyRanker().Rank(series);

        Assert.Empty(anomalies);
    }

    [Fact]
    public void Rank_HandlesZeroVarianceBaseline_WithoutDivideByZero()
    {
        // Every baseline day costs exactly the same, so MAD == 0. The ranker
        // has an explicit branch for this (robustZ = 6 when delta > 0, else
        // 0) specifically to avoid a division by zero -- this test exists
        // to keep that branch from silently regressing into a crash or an
        // always-true/always-false detector.
        var start = new DateOnly(2026, 7, 1);
        var flatSeries = Enumerable.Range(0, 8)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", 100, "USD", 0.99, "tag:team"))
            .ToArray();

        var noSpike = new CostAnomalyRanker().Rank(flatSeries);
        Assert.Empty(noSpike);

        var withSpike = flatSeries
            .Concat(new[] { new ChargebackAllocation(start.AddDays(8), "payments", "AKS", "sub-a", 500, "USD", 0.99, "tag:team") })
            .ToArray();
        var spikeResult = new CostAnomalyRanker().Rank(withSpike);
        Assert.Single(spikeResult);
    }

    [Fact]
    public void Rank_TreatsDifferentCurrenciesAsIndependentSeries()
    {
        // Grouping is by (Team, ServiceName, Currency). A USD spike must not
        // be washed out -- or falsely triggered -- by an EUR series for the
        // same team/service, and vice versa.
        var start = new DateOnly(2026, 7, 1);
        var usd = Enumerable.Range(0, 9)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", i == 8 ? 300 : 100, "USD", 0.99, "tag:team"));
        var eur = Enumerable.Range(0, 9)
            .Select(i => new ChargebackAllocation(start.AddDays(i), "payments", "AKS", "sub-a", 90, "EUR", 0.99, "tag:team"));

        var anomalies = new CostAnomalyRanker().Rank(usd.Concat(eur)).ToArray();

        var anomaly = Assert.Single(anomalies);
        Assert.Equal(300m, anomaly.ActualCost);
        Assert.Contains("Ensemble anomaly", anomaly.Explanation);
    }
}
