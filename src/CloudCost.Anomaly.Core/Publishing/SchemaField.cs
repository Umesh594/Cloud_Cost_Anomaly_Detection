namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// Describes the lifecycle of a single field within an <see cref="EventSchema"/>: when it was introduced
/// and, if it was later removed or superseded, when it stopped being part of the contract.
/// </summary>
public sealed record SchemaField(string Name, bool IsRequired, int IntroducedInVersion, int? RemovedInVersion = null)
{
    /// <summary>True when this field is part of the schema as of the given version.</summary>
    public bool IsActiveAt(int version) => IntroducedInVersion <= version && (RemovedInVersion is null || RemovedInVersion > version);
}
