using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies;

public interface IAbacPolicy
{
  string Name { get; }
  string ResourceType { get; }
  string Relation { get; }

  bool Evaluate(
      SubjectAttributes subject,
      ResourceAttributes resource,
      EnvironmentAttributes environment);
}