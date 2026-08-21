using CloudCost.Anomaly.Core;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class DataGovernancePolicyTests
{
    [Fact]
    public void Normalize_HashesIdentifiersAndKeepsOnlyApprovedTags()
    {
        var records = new[]
        {
            new CostRecord(
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                "sub-production-123",
                "/subscriptions/sub-production-123/resourceGroups/payments-prod/providers/Microsoft.Compute/virtualMachines/vm-42",
                "Compute",
                "Virtual Machines",
                "USD",
                125.50m,
                new Dictionary<string, string>
                {
                    ["team"] = "payments",
                    ["ownerEmail"] = "lead@example.com",
                    ["costCenter"] = "cc-100"
                })
        };

        var normalized = new DataGovernancePolicy().Normalize(records).Single();

        Assert.NotEqual("sub-production-123", normalized.SubscriptionId);
        Assert.StartsWith("Microsoft.Compute/virtualMachines/", normalized.ResourceId);
        Assert.Contains("team", normalized.Tags.Keys);
        Assert.Contains("costcenter", normalized.Tags.Keys);
        Assert.DoesNotContain("ownerEmail", normalized.Tags.Keys);
    }

    [Fact]
    public void Normalize_RedactsContactDataFromAllowedTags()
    {
        var records = new[]
        {
            new CostRecord(
                DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                "sub-a",
                "resource-a",
                "Storage",
                "Storage Accounts",
                "USD",
                20m,
                new Dictionary<string, string> { ["owner-team"] = "payments +1 425 555 0100" })
        };

        var normalized = new DataGovernancePolicy().Normalize(records).Single();

        Assert.Equal("payments [phone]", normalized.Tags["owner-team"]);
    }

    [Fact]
    public void Normalize_HashesAllocationSubscriptionBeforeTelemetryExport()
    {
        var allocations = new[]
        {
            new ChargebackAllocation(new DateOnly(2026, 8, 1), "payments", "sub-production-123", "AKS", 500m, "USD", 0.99, "tag:team")
        };

        var normalized = new DataGovernancePolicy().Normalize(allocations).Single();

        Assert.NotEqual("sub-production-123", normalized.SubscriptionId);
        Assert.Equal("payments", normalized.Team);
        Assert.Equal(500m, normalized.Cost);
    }
}
