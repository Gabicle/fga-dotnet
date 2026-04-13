namespace Permissions.Domain.ValueObjects;

public sealed record TupleKey
{
  public string ObjectType { get; }
  public string ObjectId { get; }
  public string Relation { get; }
  public string SubjectType { get; }
  public string SubjectId { get; }
  public string? SubjectRelation { get; }

  public TupleKey(
      string objectType,
      string objectId,
      string relation,
      string subjectType,
      string subjectId,
      string? subjectRelation = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
    ArgumentException.ThrowIfNullOrWhiteSpace(relation);
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

    ObjectType = objectType;
    ObjectId = objectId;
    Relation = relation;
    SubjectType = subjectType;
    SubjectId = subjectId;
    SubjectRelation = subjectRelation;
  }

  public override string ToString() =>
      $"{ObjectType}:{ObjectId}#{Relation}@{SubjectType}:{SubjectId}" +
      (SubjectRelation is not null ? $"#{SubjectRelation}" : string.Empty);
}