namespace Permissions.Domain.Attributes;

public sealed class SubjectAttributes
{
  public string SubjectType { get; init; } = null!;
  public string SubjectId { get; init; } = null!;
  public string? Region { get; init; }
  public string? Department { get; init; }
  public int? ClearanceLevel { get; init; }
  public Dictionary<string, string> Custom { get; init; } = [];
}