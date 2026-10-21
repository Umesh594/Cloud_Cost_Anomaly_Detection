using System.Collections.Concurrent;

namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// Tracks which (sink, event id) pairs have already been delivered so at-least-once redelivery (retries,
/// duplicate publishes within the retention window, worker-cycle overlap) does not double-apply an event
/// at a sink that cares about exactly-once effects, such as an ADX ingest.
///
/// Lock-free and safe under concurrent callers: acquisition is a single atomic CAS per key via
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, so two threads racing to acquire the same key can
/// never both win. Entries are TTL-bound; call <see cref="EvictExpired"/> periodically (the fan-out
/// publisher does this on a cadence) to bound memory for a long-running process.
/// </summary>
public sealed class IdempotencyStore(TimeProvider? timeProvider = null)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> expiryByKey = new(StringComparer.Ordinal);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public int Count => expiryByKey.Count;

    /// <summary>
    /// Attempts to claim first-delivery rights for <paramref name="eventId"/> at <paramref name="sinkName"/>.
    /// Returns <c>true</c> the first time a (sink, event id) pair is seen, or once its previous claim has
    /// expired; returns <c>false</c> for a duplicate still within its retention window.
    /// </summary>
    public bool TryAcquire(string sinkName, string eventId, TimeSpan retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Retention must be positive.");
        }

        var key = string.Concat(sinkName, ":", eventId);
        var now = clock.GetUtcNow();
        var newExpiry = now + retention;

        while (true)
        {
            if (!expiryByKey.TryGetValue(key, out var existingExpiry))
            {
                // No prior claim: try to be the first.
                if (expiryByKey.TryAdd(key, newExpiry))
                {
                    return true;
                }

                // Lost the race to another thread inserting concurrently - re-evaluate against what won.
                continue;
            }

            if (existingExpiry >= now)
            {
                // Still within the previous claim's retention window: genuine duplicate.
                return false;
            }

            // The previous claim has expired; atomically replace it so only one racing caller wins.
            if (expiryByKey.TryUpdate(key, newExpiry, existingExpiry))
            {
                return true;
            }
        }
    }

    /// <summary>Removes claims past their retention window. Returns the number of entries evicted.</summary>
    public int EvictExpired()
    {
        var now = clock.GetUtcNow();
        var evicted = 0;
        foreach (var (key, expiry) in expiryByKey)
        {
            if (expiry < now && expiryByKey.TryRemove(new KeyValuePair<string, DateTimeOffset>(key, expiry)))
            {
                evicted++;
            }
        }

        return evicted;
    }
}
