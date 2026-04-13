using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies.BuiltIn;

public sealed class SensitivityLevelPolicy : IAbacPolicy
{
  public string Name => "SensitivityLevelPolicy";
  public string ResourceType { get; }
  public string Relation { get; }

  public SensitivityLevelPolicy(string resourceType, string relation)
  {
    ResourceType = resourceType;
    Relation = relation;
  }

  public bool Evaluate(
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment)
  {
    if (subject.ClearanceLevel is null || resource.SensitivityLevel is null)
      return true;

    return subject.ClearanceLevel >= resource.SensitivityLevel;
  }
}