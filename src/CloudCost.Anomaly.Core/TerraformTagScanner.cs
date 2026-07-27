using System.Text.RegularExpressions;

namespace CloudCost.Anomaly.Core;

public sealed partial class TerraformTagScanner
{
    private readonly IReadOnlyCollection<TeamPolicy> policies;

    public TerraformTagScanner(IEnumerable<TeamPolicy> policies)
    {
        this.policies = policies.ToArray();
    }

    public IReadOnlyCollection<TerraformTagFinding> Scan(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(root, "*.tf", SearchOption.AllDirectories).ToArray();
        var sharedTagsHaveOwnership = files.Any(file => LocalTagsOwnershipRegex().IsMatch(File.ReadAllText(file)));

        return files
            .SelectMany(file => ScanFile(file, sharedTagsHaveOwnership))
            .ToArray();
    }

    private IEnumerable<TerraformTagFinding> ScanFile(string filePath, bool sharedTagsHaveOwnership)
    {
        var content = File.ReadAllText(filePath);
        foreach (Match resource in ResourceBlockRegex().Matches(content))
        {
            var resourceName = $"{resource.Groups["type"].Value}.{resource.Groups["name"].Value}";
            var body = resource.Groups["body"].Value;
            if (!RequiresOwnershipTag(resource.Groups["type"].Value))
            {
                continue;
            }

            if (TeamTagRegex().IsMatch(body) || sharedTagsHaveOwnership && LocalTagsReferenceRegex().IsMatch(body))
            {
                continue;
            }

            var suggestion = InferTeam(resourceName, body);
            yield return new TerraformTagFinding(
                filePath,
                resourceName,
                "Resource has no team/cost-center tag; chargeback will use inferred ownership.",
                suggestion.Team,
                suggestion.Confidence);
        }
    }

    private TeamResolution InferTeam(string resourceName, string body)
    {
        var signal = $"{resourceName} {body}";
        foreach (var policy in policies)
        {
            if (policy.ResourceGroupPrefixes.Any(prefix => signal.Contains(prefix, StringComparison.OrdinalIgnoreCase)) ||
                policy.ServiceHints.Any(hint => signal.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                return new TeamResolution(policy.Team, 0.72, "iac-scan");
            }
        }

        return new TeamResolution("unallocated", 0.3, "iac-scan");
    }

    private static bool RequiresOwnershipTag(string resourceType)
    {
        return resourceType is not
            "azurerm_role_assignment" and not
            "azurerm_federated_identity_credential" and not
            "azurerm_kusto_database";
    }

    [GeneratedRegex("""resource\s+"(?<type>[^"]+)"\s+"(?<name>[^"]+)"\s*\{(?<body>[\s\S]*?)\n\}""", RegexOptions.Multiline)]
    private static partial Regex ResourceBlockRegex();

    [GeneratedRegex(@"(?i)(team|owner|cost[-_]?center|business[-_]?unit)\s*=\s*""")]
    private static partial Regex TeamTagRegex();

    [GeneratedRegex(@"(?is)tags\s*=\s*\{[\s\S]*?(team|owner|cost[-_]?center|business[-_]?unit)\s*=\s*""")]
    private static partial Regex LocalTagsOwnershipRegex();

    [GeneratedRegex("""(?i)tags\s*=\s*local\.tags""")]
    private static partial Regex LocalTagsReferenceRegex();
}
