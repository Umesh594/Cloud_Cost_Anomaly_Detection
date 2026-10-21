using CloudCost.Anomaly.Core.Publishing;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class IdempotencyStoreTests
{
    [Fact]
    public void TryAcquire_FirstDelivery_Succeeds()
    {
        var store = new IdempotencyStore();

        var acquired = store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        Assert.True(acquired);
    }

    [Fact]
    public void TryAcquire_DuplicateWithinRetention_IsRejected()
    {
        var store = new IdempotencyStore();
        store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        var acquiredAgain = store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        Assert.False(acquiredAgain);
    }

    [Fact]
    public void TryAcquire_SameEventDifferentSink_IsIndependentlyAcquirable()
    {
        var store = new IdempotencyStore();
        store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        var acquiredForWebhook = store.TryAcquire("webhook", "evt-1", TimeSpan.FromMinutes(10));

        Assert.True(acquiredForWebhook);
    }

    [Fact]
    public void TryAcquire_AfterRetentionExpires_CanBeReacquired()
    {
        var fakeClock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new IdempotencyStore(fakeClock);
        store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        fakeClock.Advance(TimeSpan.FromMinutes(11));
        var reacquired = store.TryAcquire("adx", "evt-1", TimeSpan.FromMinutes(10));

        Assert.True(reacquired);
    }

    [Fact]
    public void EvictExpired_RemovesOnlyExpiredEntries()
    {
        var fakeClock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new IdempotencyStore(fakeClock);
        store.TryAcquire("adx", "expires-soon", TimeSpan.FromMinutes(5));

        fakeClock.Advance(TimeSpan.FromMinutes(6));
        store.TryAcquire("adx", "still-fresh", TimeSpan.FromMinutes(5));

        var evicted = store.EvictExpired();

        Assert.Equal(1, evicted);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task TryAcquire_ConcurrentCallersForSameKey_OnlyOneWins()
    {
        var store = new IdempotencyStore();
        const int callers = 64;
        using var barrier = new Barrier(callers);

        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return store.TryAcquire("adx", "evt-race", TimeSpan.FromMinutes(10));
        })));

        Assert.Equal(1, results.Count(acquired => acquired));
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
