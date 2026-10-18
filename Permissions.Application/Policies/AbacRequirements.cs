using Permissions.Application.Policies.BuiltIn;

namespace Permissions.Application.Policies;

// Server-owned requirements. Unlisted object-type/relation pairs are ReBAC-only.
public static class AbacRequirements
{
  public sealed record Requirement(
      IReadOnlyList<Type> PolicyTypes,
      bool RequiresStatus);

  private static readonly IReadOnlyDictionary<(string ObjectType, string Relation), Requirement>
      RequiredOperations = new Dictionary<(string, string), Requirement>
      {
        [("report", "viewer")] = new(
            [typeof(RegionMatchPolicy), typeof(SensitivityLevelPolicy)], false),
        [("report", "editor")] = new(
            [typeof(RegionMatchPolicy), typeof(ResourceStatusPolicy),
             typeof(SensitivityLevelPolicy)], true)
      };

  // These are the only report statuses currently documented or used by tests.
  private static readonly HashSet<string> SupportedReportStatuses =
      new(["draft", "locked", "archived"], StringComparer.OrdinalIgnoreCase);

  public static Requirement? For(string objectType, string relation) =>
      RequiredOperations.TryGetValue((objectType, relation), out var requirement)
          ? requirement : null;

  public static bool IsSupportedReportStatus(string status) =>
      SupportedReportStatuses.Contains(status);

  public static void ValidateRegisteredPolicies(IEnumerable<IAbacPolicy> policies)
  {
    var registered = policies.ToList();
    foreach (var (operation, requirement) in RequiredOperations)
    {
      if (requirement.PolicyTypes.Any(type => !registered.Any(policy =>
              policy.GetType() == type && policy.ResourceType == operation.ObjectType &&
              policy.Relation == operation.Relation)))
        throw new InvalidOperationException(
            $"Required ABAC policy registration is missing for {operation.ObjectType}/{operation.Relation}.");
    }
  }
}
