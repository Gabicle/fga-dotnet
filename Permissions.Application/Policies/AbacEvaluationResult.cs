namespace Permissions.Application.Policies;

public sealed record AbacEvaluationResult
{
  public bool IsApplicable { get; init; }
  public bool IsAllowed { get; init; }
  public string? DeniedByPolicy { get; init; }

  public static AbacEvaluationResult NotApplicable() =>
      new() { IsApplicable = false, IsAllowed = true };

  public static AbacEvaluationResult Allowed() =>
      new() { IsApplicable = true, IsAllowed = true };

  public static AbacEvaluationResult Denied(string policyName) =>
      new() { IsApplicable = true, IsAllowed = false, DeniedByPolicy = policyName };
}