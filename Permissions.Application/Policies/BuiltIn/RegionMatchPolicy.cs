using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies.BuiltIn;

public sealed class RegionMatchPolicy : IAbacPolicy
{
  public string Name => "RegionMatchPolicy";
  public string ResourceType { get; }
  public string Relation { get; }

  public RegionMatchPolicy(string resourceType, string relation)
  {
    ResourceType = resourceType;
    Relation = relation;
  }

  public bool Evaluate(
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment)
  {
    if (subject.Region is null || resource.Region is null)
      return true;

    return subject.Region.Equals(resource.Region, StringComparison.OrdinalIgnoreCase);
  }
}