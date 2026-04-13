namespace Permissions.Domain.Attributes;

public sealed class ResourceAttributes
{
  public string ResourceType { get; init; } = null!;
  public string ResourceId { get; init; } = null!;
  public string? Region { get; init; }
  public string? Department { get; init; }
  public string? Status { get; init; }
  public int? SensitivityLevel { get; init; }
  public string? OwnerId { get; init; }
  public Dictionary<string, string> Custom { get; init; } = [];
}