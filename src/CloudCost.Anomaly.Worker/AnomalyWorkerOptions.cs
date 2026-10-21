namespace CloudCost.Anomaly.Worker;

public sealed class AnomalyWorkerOptions
{
    public string TenantId { get; set; } = string.Empty;
    public string BillingScope { get; set; } = string.Empty;
    public string AdxClusterUri { get; set; } = string.Empty;
    public string AdxDatabase { get; set; } = "CloudCost";
    public string CostTable { get; set; } = "CostUsage";
    public string TerraformRoot { get; set; } = "infra/terraform";
    public int PollIntervalMinutes { get; set; } = 60;
    public int LookbackDays { get; set; } = 35;
    public decimal AlertMinimumCostDelta { get; set; } = 100;
}
