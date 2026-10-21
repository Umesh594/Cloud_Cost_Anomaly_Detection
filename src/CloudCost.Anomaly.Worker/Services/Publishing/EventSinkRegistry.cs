using Microsoft.Extensions.Options;

namespace CloudCost.Anomaly.Worker.Services.Publishing;

/// <summary>
/// Resolves the set of <see cref="IEventSink"/> implementations that are active, purely from the
/// <c>EventPublishing:EnabledSinks</c> configuration list. All sinks are still registered in DI
/// regardless of configuration; this registry is the single place that decides which ones actually
/// receive events, so onboarding a destination is a config change, not an if/else in the pipeline.
/// </summary>
public sealed class EventSinkRegistry
{
    public EventSinkRegistry(IEnumerable<IEventSink> sinks, IOptions<EventPublishingOptions> options)
    {
        var enabledNames = new HashSet<string>(options.Value.EnabledSinks, StringComparer.OrdinalIgnoreCase);
        var allSinks = sinks.ToArray();

        var duplicateNames = allSinks
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"Multiple IEventSink implementations registered the same name(s): {string.Join(", ", duplicateNames)}.");
        }

        var unknownNames = enabledNames
            .Where(name => !allSinks.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (unknownNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"EventPublishing:EnabledSinks references unknown sink name(s): {string.Join(", ", unknownNames)}.");
        }

        ActiveSinks = allSinks.Where(s => enabledNames.Contains(s.Name)).ToArray();
    }

    public IReadOnlyList<IEventSink> ActiveSinks { get; }
}
