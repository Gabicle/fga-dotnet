namespace Permissions.Domain.Entities;

public sealed class RelationTuple
{
  public Guid Id { get; private set; }
  public string ObjectType { get; private set; } = null!;
  public string ObjectId { get; private set; } = null!;
  public string Relation { get; private set; } = null!;
  public string SubjectType { get; private set; } = null!;
  public string SubjectId { get; private set; } = null!;
  public string? SubjectRelation { get; private set; }
  public DateTime CreatedAt { get; private set; }
  public DateTime? ExpiresAt { get; private set; }

  private RelationTuple() { }

  public static RelationTuple Create(
      string objectType,
      string objectId,
      string relation,
      string subjectType,
      string subjectId,
      string? subjectRelation = null,
      DateTime? expiresAt = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
    ArgumentException.ThrowIfNullOrWhiteSpace(relation);
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

    if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
      throw new InvalidOperationException("ExpiresAt must be a future date.");

    return new RelationTuple
    {
      Id = Guid.NewGuid(),
      ObjectType = objectType,
      ObjectId = objectId,
      Relation = relation,
      SubjectType = subjectType,
      SubjectId = subjectId,
      SubjectRelation = subjectRelation,
      CreatedAt = DateTime.UtcNow,
      ExpiresAt = expiresAt
    };
  }

  public bool IsExpired() =>
      ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;

  public override string ToString() =>
      $"{ObjectType}:{ObjectId}#{Relation}@{SubjectType}:{SubjectId}" +
      (SubjectRelation is not null ? $"#{SubjectRelation}" : string.Empty);
}