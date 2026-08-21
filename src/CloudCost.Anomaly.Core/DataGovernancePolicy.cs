using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CloudCost.Anomaly.Core;

public sealed class DataGovernancePolicy
{
    private static readonly Regex Email = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"\+?\d[\d\s().-]{7,}\d", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "application",
        "costcenter",
        "cost-center",
        "environment",
        "owner-team",
        "team"
    };

    public IReadOnlyCollection<CostRecord> Normalize(IReadOnlyCollection<CostRecord> records)
    {
        return records.Select(record => record with
        {
            SubscriptionId = StableHash(record.SubscriptionId),
            ResourceId = NormalizeResourceId(record.ResourceId),
            Tags = NormalizeTags(record.Tags)
        }).ToArray();
    }

    public IReadOnlyCollection<ChargebackAllocation> Normalize(IReadOnlyCollection<ChargebackAllocation> allocations)
    {
        return allocations.Select(allocation => allocation with
        {
            SubscriptionId = StableHash(allocation.SubscriptionId)
        }).ToArray();
    }

    public static string StableHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string NormalizeResourceId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "unknown";
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var providerIndex = Array.FindIndex(segments, segment => string.Equals(segment, "providers", StringComparison.OrdinalIgnoreCase));
        if (providerIndex < 0 || providerIndex + 2 >= segments.Length)
        {
            return "resource:" + StableHash(resourceId);
        }

        var provider = segments[providerIndex + 1];
        var type = segments[providerIndex + 2];
        return $"{provider}/{type}/{StableHash(resourceId)}";
    }

    private static IReadOnlyDictionary<string, string> NormalizeTags(IReadOnlyDictionary<string, string> tags)
    {
        return tags
            .Where(tag => AllowedTags.Contains(tag.Key))
            .ToDictionary(tag => tag.Key.ToLowerInvariant(), tag => Redact(tag.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static string Redact(string value)
    {
        var redacted = Email.Replace(value, "[email]");
        redacted = Phone.Replace(redacted, "[phone]");
        return redacted.Length <= 96 ? redacted : redacted[..96];
    }
}
