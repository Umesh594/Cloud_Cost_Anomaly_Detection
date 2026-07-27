using CloudCost.Anomaly.Core;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class ChargebackEngineTests
{
    [Fact]
    public void Allocate_GroupsByTeamAndService()
    {
        var engine = new ChargebackEngine([
            new TeamPolicy("payments", ["cc-100"], ["sub-a"], ["pay-"], ["aks"])
        ]);

        var records = new[]
        {
            Record(10, "Virtual Machines", new Dictionary<string, string> { ["team"] = "payments" }),
            Record(5, "Virtual Machines", new Dictionary<string, string> { ["team"] = "payments" }),
            Record(4, "Storage", new Dictionary<string, string>())
        };

        var allocations = engine.Allocate(records);

        Assert.Contains(allocations, x => x.Team == "payments" && x.ServiceName == "Virtual Machines" && x.Cost == 15);
        Assert.Contains(allocations, x => x.Team == "payments" && x.ServiceName == "Storage" && x.Cost == 4);
    }

    [Fact]
    public void ResolveTeam_UsesCostCenterAlias()
    {
        var engine = new ChargebackEngine([
            new TeamPolicy("ai-platform", ["cc-ai"], [], [], [])
        ]);

        var resolution = engine.ResolveTeam(Record(10, "Azure OpenAI", new Dictionary<string, string> { ["cost-center"] = "cc-ai" }));

        Assert.Equal("ai-platform", resolution.Team);
        Assert.Equal("tag:costcenter", resolution.Source);
    }

    [Fact]
    public void ResolveTeam_FallsBackToUnallocatedWhenOwnershipSignalsAreMissing()
    {
        var engine = new ChargebackEngine([
            new TeamPolicy("payments", ["cc-100"], ["sub-a"], ["pay-"], ["aks"])
        ]);

        var resolution = engine.ResolveTeam(new CostRecord(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            "sub-z",
            "/subscriptions/sub-z/resourceGroups/shared/providers/Microsoft.Compute/virtualMachines/vm1",
            "Compute",
            "Virtual Machines",
            "USD",
            10,
            new Dictionary<string, string>()));

        Assert.Equal("unallocated", resolution.Team);
        Assert.Equal("fallback", resolution.Source);
        Assert.True(resolution.Confidence < 0.5);
    }

    private static CostRecord Record(decimal cost, string service, IReadOnlyDictionary<string, string> tags)
    {
        return new CostRecord(
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            "sub-a",
            "/subscriptions/sub-a/resourceGroups/pay-prod/providers/Microsoft.Compute/virtualMachines/vm1",
            "Compute",
            service,
            "USD",
            cost,
            tags);
    }
}
