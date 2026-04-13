using Permissions.Domain.Attributes;

namespace Permissions.Application.Policies;

public sealed class AbacContext
{
  public SubjectAttributes Subject { get; init; } = null!;
  public ResourceAttributes Resource { get; init; } = null!;
  public EnvironmentAttributes Environment { get; init; } = new();
}