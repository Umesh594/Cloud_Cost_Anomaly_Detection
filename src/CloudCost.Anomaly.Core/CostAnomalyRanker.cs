namespace CloudCost.Anomaly.Core;

public sealed class CostAnomalyRanker
{
    private const int LookbackDays = 7;
    private const double AlertThreshold = 0.65;

    public IReadOnlyCollection<AnomalyScore> Rank(IEnumerable<ChargebackAllocation> allocations)
    {
        return allocations
            .GroupBy(x => new { x.Team, x.ServiceName, x.Currency })
            .SelectMany(RankSeries)
            .Where(x => x.Severity >= AlertThreshold)
            .OrderByDescending(x => x.Severity)
            .ToArray();
    }

    private static IEnumerable<AnomalyScore> RankSeries(IEnumerable<ChargebackAllocation> series)
    {
        var ordered = series.OrderBy(x => x.Date).ToArray();
        if (ordered.Length < LookbackDays + 1)
        {
            yield break;
        }

        for (var i = LookbackDays; i < ordered.Length; i++)
        {
            var current = ordered[i];
            var baseline = ordered.Skip(i - LookbackDays).Take(LookbackDays).Select(x => x.Cost).ToArray();
            var median = Median(baseline);
            var mad = Median(baseline.Select(x => Math.Abs(x - median)).ToArray());
            var ewma = Ewma(baseline, 0.35m);
            var dayOfWeekPeer = ordered
                .Take(i)
                .Where(x => x.Date.DayOfWeek == current.Date.DayOfWeek)
                .Select(x => x.Cost)
                .DefaultIfEmpty(median)
                .Average();
            var expected = Math.Round((median * 0.5m) + (ewma * 0.3m) + (dayOfWeekPeer * 0.2m), 2);
            var delta = current.Cost - expected;
            var robustZ = mad == 0 ? (delta > 0 ? 6 : 0) : (double)(delta / (mad * 1.4826m));
            var pctJump = expected == 0 ? 1 : (double)((current.Cost - expected) / expected);
            var persistence = PersistenceScore(ordered, i, expected);
            var impactScore = Math.Clamp((double)(delta / 100m), 0, 1);
            var severity = Math.Clamp(
                (Math.Max(robustZ, 0) / 8.0 * 0.45)
                + (Math.Max(pctJump, 0) * 0.25)
                + (persistence * 0.15)
                + (impactScore * 0.15),
                0,
                1);

            if (delta > 0 && severity >= AlertThreshold)
            {
                yield return new AnomalyScore(
                    current.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    current.Team,
                    current.ServiceName,
                    current.Cost,
                    expected,
                    Math.Round(severity, 3),
                    $"Ensemble anomaly: {pctJump:P0} above expected cost; robust_z={robustZ:F2}; persistence={persistence:F2}; impact=${delta:F2}; attribution={current.AttributionSource}; confidence={current.Confidence:P0}");
            }
        }
    }

    private static decimal Ewma(IReadOnlyList<decimal> values, decimal alpha)
    {
        var current = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            current = (alpha * values[i]) + ((1 - alpha) * current);
        }
        return current;
    }

    private static double PersistenceScore(IReadOnlyList<ChargebackAllocation> ordered, int currentIndex, decimal expected)
    {
        var start = Math.Max(0, currentIndex - 2);
        var recent = ordered.Skip(start).Take(currentIndex - start + 1).ToArray();
        var elevated = recent.Count(x => x.Cost > expected * 1.15m);
        return Math.Clamp(elevated / 3.0, 0, 1);
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2 : ordered[mid];
    }
}
