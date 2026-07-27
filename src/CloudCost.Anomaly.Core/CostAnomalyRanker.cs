namespace CloudCost.Anomaly.Core;

public sealed class CostAnomalyRanker
{
    public IReadOnlyCollection<AnomalyScore> Rank(IEnumerable<ChargebackAllocation> allocations)
    {
        return allocations
            .GroupBy(x => new { x.Team, x.ServiceName, x.Currency })
            .SelectMany(RankSeries)
            .Where(x => x.Severity >= 0.65)
            .OrderByDescending(x => x.Severity)
            .ToArray();
    }

    private static IEnumerable<AnomalyScore> RankSeries(IEnumerable<ChargebackAllocation> series)
    {
        var ordered = series.OrderBy(x => x.Date).ToArray();
        if (ordered.Length < 8)
        {
            yield break;
        }

        for (var i = 7; i < ordered.Length; i++)
        {
            var current = ordered[i];
            var baseline = ordered.Skip(i - 7).Take(7).Select(x => x.Cost).ToArray();
            var median = Median(baseline);
            var mad = Median(baseline.Select(x => Math.Abs(x - median)).ToArray());
            var expected = median;
            var delta = current.Cost - expected;
            var robustZ = mad == 0 ? (delta > 0 ? 6 : 0) : (double)(delta / (mad * 1.4826m));
            var pctJump = expected == 0 ? 1 : (double)((current.Cost - expected) / expected);
            var severity = Math.Clamp((robustZ / 8.0 * 0.7) + (pctJump * 0.3), 0, 1);

            if (delta > 0 && severity >= 0.65)
            {
                yield return new AnomalyScore(
                    current.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    current.Team,
                    current.ServiceName,
                    current.Cost,
                    expected,
                    Math.Round(severity, 3),
                    $"Cost is {pctJump:P0} above rolling median; attribution={current.AttributionSource}; confidence={current.Confidence:P0}");
            }
        }
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2 : ordered[mid];
    }
}
