namespace CloudCost.Anomaly.Core;

public sealed record TerraformTagFinding(
    string FilePath,
    string ResourceName,
    string Message,
    string SuggestedTeam,
    double Confidence);
