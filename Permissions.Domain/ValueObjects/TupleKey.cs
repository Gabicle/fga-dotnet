namespace Permissions.Domain.ValueObjects;

public sealed record TupleKey
{
  public string ObjectType { get; }
  public string ObjectId { get; }
  public string Relation { get; }
  public string SubjectType { get; }
  public string SubjectId { get; }

  public TupleKey(
      string objectType,
      string objectId,
      string relation,
      string subjectType,
      string subjectId)
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
  }

  public override string ToString() =>
      $"{ObjectType}:{ObjectId}#{Relation}@{SubjectType}:{SubjectId}";

  public static TupleKey Parse(string tupleString)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(tupleString);

    try
    {
      var objectPart = tupleString.Split('#')[0];
      var relationAndSubject = tupleString.Split('#')[1];
      var relation = relationAndSubject.Split('@')[0];
      var subjectPart = relationAndSubject.Split('@')[1];

      var objectType = objectPart.Split(':')[0];
      var objectId = objectPart.Split(':')[1];
      var subjectType = subjectPart.Split(':')[0];
      var subjectId = subjectPart.Split(':')[1];

      return new TupleKey(objectType, objectId, relation, subjectType, subjectId);
    }
    catch
    {
      throw new FormatException(
          $"Invalid tuple format: '{tupleString}'. Expected format: objectType:objectId#relation@subjectType:subjectId");
    }
  }
}