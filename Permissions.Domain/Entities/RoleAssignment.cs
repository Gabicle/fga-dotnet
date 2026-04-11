namespace Permissions.Domain.Entities;

public sealed class RoleAssignment
{
  public Guid Id { get; private set; }
  public Guid RoleId { get; private set; }
  public Role Role { get; private set; } = null!;
  public string SubjectType { get; private set; } = null!;
  public string SubjectId { get; private set; } = null!;
  public string? ResourceType { get; private set; }
  public string? ResourceId { get; private set; }
  public DateTime CreatedAt { get; private set; }
  public DateTime? ExpiresAt { get; private set; }

  private RoleAssignment() { }

  public static RoleAssignment Create(
      Guid roleId,
      string subjectType,
      string subjectId,
      string? resourceType = null,
      string? resourceId = null,
      DateTime? expiresAt = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

    if (resourceType is null != resourceId is null)
      throw new InvalidOperationException(
          "ResourceType and ResourceId must both be provided or both be null.");

    if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
      throw new InvalidOperationException("ExpiresAt must be a future date.");

    return new RoleAssignment
    {
      Id = Guid.NewGuid(),
      RoleId = roleId,
      SubjectType = subjectType,
      SubjectId = subjectId,
      ResourceType = resourceType,
      ResourceId = resourceId,
      CreatedAt = DateTime.UtcNow,
      ExpiresAt = expiresAt
    };
  }

  public bool IsExpired() =>
      ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;

  public bool IsGlobal() =>
      ResourceType is null && ResourceId is null;
}