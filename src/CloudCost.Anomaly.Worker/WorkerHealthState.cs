namespace CloudCost.Anomaly.Worker;

public sealed class WorkerHealthState
{
    private readonly object gate = new();

    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastSucceededAt { get; private set; }
    public DateTimeOffset? LastFailedAt { get; private set; }
    public string? LastError { get; private set; }

    public void MarkStarted(DateTimeOffset timestamp)
    {
        lock (gate)
        {
            LastStartedAt = timestamp;
        }
    }

    public void MarkSucceeded(DateTimeOffset timestamp)
    {
        lock (gate)
        {
            LastSucceededAt = timestamp;
            LastError = null;
        }
    }

    public void MarkFailed(DateTimeOffset timestamp, Exception exception)
    {
        lock (gate)
        {
            LastFailedAt = timestamp;
            LastError = exception.Message;
        }
    }
}
