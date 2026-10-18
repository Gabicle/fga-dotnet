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
      string objectType,
      string relation,
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment,
      AbacRequirements.Requirement? requirement = null)
  {
    var applicablePolicies = _policies
        .Where(p => p.ResourceType == objectType &&
                    p.Relation == relation)
        .ToList();

    if (applicablePolicies.Count == 0 ||
        (requirement is not null && requirement.PolicyTypes.Any(type =>
            !applicablePolicies.Any(policy => policy.GetType() == type))))
      return AbacEvaluationResult.NotApplicable();

    foreach (var policy in applicablePolicies)
    {
      if (!policy.Evaluate(subject, resource, environment))
        return AbacEvaluationResult.Denied(policy.Name);
    }

    return AbacEvaluationResult.Allowed();
  }

  public void ValidateRequiredPolicies() =>
      AbacRequirements.ValidateRegisteredPolicies(_policies);
}
