using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies;

public sealed class AbacEvaluator
{
  private readonly IReadOnlyList<IAbacPolicy> _policies;

  public AbacEvaluator(IEnumerable<IAbacPolicy> policies)
  {
    _policies = policies.ToList().AsReadOnly();
  }

  public AbacEvaluationResult Evaluate(
      string relation,
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment)
  {
    var applicablePolicies = _policies
        .Where(p => p.ResourceType == resource.ResourceType &&
                    p.Relation == relation)
        .ToList();

    if (applicablePolicies.Count == 0)
      return AbacEvaluationResult.NotApplicable();

    foreach (var policy in applicablePolicies)
    {
      if (!policy.Evaluate(subject, resource, environment))
        return AbacEvaluationResult.Denied(policy.Name);
    }

    return AbacEvaluationResult.Allowed();
  }
}