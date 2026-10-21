namespace CloudCost.Anomaly.Core.Publishing;

/// <summary>
/// The result of checking whether an event produced at the registry's current schema version can be
/// consumed by a sink that only understands up to <c>sinkSupportedVersion</c>.
/// </summary>
public sealed record SchemaCompatibilityResult(
    bool IsCompatible,
    IReadOnlyList<string> MissingRequiredFields,
    IReadOnlyList<string> FieldsUnknownToSink)
{
    public static SchemaCompatibilityResult Compatible { get; } = new(true, [], []);
}
