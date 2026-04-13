using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies.BuiltIn;

public sealed class ResourceStatusPolicy : IAbacPolicy
{
  public string Name => "ResourceStatusPolicy";
  public string ResourceType { get; }
  public string Relation { get; }

  private readonly IReadOnlyList<string> _blockedStatuses;

  public ResourceStatusPolicy(
      string resourceType,
      string relation,
      params string[] blockedStatuses)
  {
    ResourceType = resourceType;
    Relation = relation;
    _blockedStatuses = blockedStatuses.ToList().AsReadOnly();
  }

  public bool Evaluate(
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment)
  {
    if (resource.Status is null)
      return true;

    return !_blockedStatuses.Any(s =>
        s.Equals(resource.Status, StringComparison.OrdinalIgnoreCase));
  }
}