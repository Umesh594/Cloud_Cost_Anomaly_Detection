using CloudCost.Anomaly.Core;
using Xunit;

namespace CloudCost.Anomaly.Tests;

public sealed class TerraformTagScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cloud-cost-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Scan_FindsResourcesMissingOwnershipTags()
    {
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "main.tf");
        File.WriteAllText(filePath, """
        resource "azurerm_kubernetes_cluster" "pay_aks" {
          name                = "pay-prod-aks"
          resource_group_name = "pay-prod"
        }

        resource "azurerm_storage_account" "tagged" {
          name = "taggedstorage"
          tags = {
            team = "payments"
          }
        }
        """);

        var scanner = new TerraformTagScanner([
            new TeamPolicy("payments", ["cc-100"], [], ["pay-"], ["aks"])
        ]);

        var finding = Assert.Single(scanner.Scan(root));
        Assert.Equal("azurerm_kubernetes_cluster.pay_aks", finding.ResourceName);
        Assert.Equal("payments", finding.SuggestedTeam);
        Assert.True(finding.Confidence >= 0.7);
    }

    [Fact]
    public void Scan_TreatsSharedLocalOwnershipTagsAsCompliant()
    {
        Directory.CreateDirectory(root);
        var localsPath = Path.Combine(root, "locals.tf");
        File.WriteAllText(localsPath, """
        locals {
          tags = {
            team        = "data-platform"
            cost-center = "cc-200"
          }
        }
        """);

        var mainPath = Path.Combine(root, "main.tf");
        File.WriteAllText(mainPath, """
        resource "azurerm_resource_group" "platform" {
          name     = "rg-finops"
          location = "eastus"
          tags     = local.tags
        }
        """);

        var scanner = new TerraformTagScanner([
            new TeamPolicy("data-platform", ["cc-200"], [], ["data-"], ["data explorer"])
        ]);

        Assert.Empty(scanner.Scan(root));
    }

    [Fact]
    public void Scan_IgnoresNonBillableIdentityAndRbacResources()
    {
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "main.tf");
        File.WriteAllText(filePath, """
        resource "azurerm_role_assignment" "reader" {
          scope                = "/subscriptions/sub-a"
          role_definition_name = "Reader"
          principal_id         = "00000000-0000-0000-0000-000000000000"
        }
        """);

        var scanner = new TerraformTagScanner([
            new TeamPolicy("payments", ["cc-100"], [], ["pay-"], ["aks"])
        ]);

        Assert.Empty(scanner.Scan(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
