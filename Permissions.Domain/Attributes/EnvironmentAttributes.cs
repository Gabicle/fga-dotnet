namespace Permissions.Domain.Attributes;

public sealed class EnvironmentAttributes
{
  public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
  public string? IpAddress { get; init; }
  public bool IsBusinessHours =>
      RequestedAt.DayOfWeek != DayOfWeek.Saturday &&
      RequestedAt.DayOfWeek != DayOfWeek.Sunday &&
      RequestedAt.Hour >= 8 &&
      RequestedAt.Hour < 18;
}