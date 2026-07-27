namespace CloudCost.Anomaly.Core;

public sealed class ChargebackEngine
{
    private static readonly string[] TeamTagKeys = ["team", "owner", "businessunit", "costcenter"];
    private readonly IReadOnlyDictionary<string, TeamPolicy> policies;

    public ChargebackEngine(IEnumerable<TeamPolicy> policies)
    {
        this.policies = policies.ToDictionary(p => p.Team, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ChargebackAllocation> Allocate(IEnumerable<CostRecord> records)
    {
        return records
            .Select(record => new { Record = record, Resolution = ResolveTeam(record) })
            .GroupBy(x => new
            {
                Date = DateOnly.FromDateTime(x.Record.UsageDate.UtcDateTime),
                x.Record.SubscriptionId,
                x.Record.ServiceName,
                x.Record.Currency,
                x.Resolution.Team,
                x.Resolution.Confidence,
                x.Resolution.Source
            })
            .Select(g => new ChargebackAllocation(
                g.Key.Date,
                g.Key.Team,
                g.Key.SubscriptionId,
                g.Key.ServiceName,
                g.Sum(x => x.Record.PretaxCost),
                g.Key.Currency,
                g.Key.Confidence,
                g.Key.Source))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Team)
            .ThenBy(x => x.ServiceName)
            .ToArray();
    }

    public TeamResolution ResolveTeam(CostRecord record)
    {
        foreach (var key in TeamTagKeys)
        {
            if (TryGetTag(record.Tags, key, out var value))
            {
                var normalizedTeam = ResolvePolicyTeam(value);
                if (normalizedTeam is not null)
                {
                    return new TeamResolution(normalizedTeam, 0.99, $"tag:{key}");
                }
            }
        }

        var resourceGroup = ExtractResourceGroup(record.ResourceId);
        foreach (var policy in policies.Values)
        {
            if (policy.Subscriptions.Contains(record.SubscriptionId, StringComparer.OrdinalIgnoreCase))
            {
                return new TeamResolution(policy.Team, 0.88, "policy:subscription");
            }

            if (resourceGroup is not null &&
                policy.ResourceGroupPrefixes.Any(prefix => resourceGroup.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return new TeamResolution(policy.Team, 0.82, "policy:resource-group");
            }

            if (policy.ServiceHints.Any(hint => record.ServiceName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                return new TeamResolution(policy.Team, 0.68, "policy:service-hint");
            }
        }

        return new TeamResolution("unallocated", 0.25, "fallback");
    }

    private string? ResolvePolicyTeam(string value)
    {
        if (policies.ContainsKey(value))
        {
            return value;
        }

        return policies.Values.FirstOrDefault(p => p.CostCenters.Contains(value, StringComparer.OrdinalIgnoreCase))?.Team;
    }

    private static bool TryGetTag(IReadOnlyDictionary<string, string> tags, string key, out string value)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key.Replace("-", string.Empty), key, StringComparison.OrdinalIgnoreCase))
            {
                value = tag.Value.Trim();
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? ExtractResourceGroup(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "resourceGroups", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }

        return null;
    }
}

public sealed record TeamResolution(string Team, double Confidence, string Source);
