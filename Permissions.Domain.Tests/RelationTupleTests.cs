using Permissions.Domain.Entities;
using Permissions.Domain.ValueObjects;

namespace Permissions.Domain.Tests;

public sealed class RelationTupleTests
{
  [Fact]
  public void Create_ValidArguments_ReturnsTuple()
  {
    var tuple = RelationTuple.Create("report", "42", "viewer", "user", "7");

    Assert.Equal("report", tuple.ObjectType);
    Assert.Equal("42", tuple.ObjectId);
    Assert.Equal("viewer", tuple.Relation);
    Assert.Equal("user", tuple.SubjectType);
    Assert.Equal("7", tuple.SubjectId);
    Assert.Null(tuple.SubjectRelation);
    Assert.NotEqual(Guid.Empty, tuple.Id);
  }

  [Theory]
  [InlineData("", "42", "viewer", "user", "7")]
  [InlineData("report", "", "viewer", "user", "7")]
  [InlineData("report", "42", "", "user", "7")]
  [InlineData("report", "42", "viewer", "", "7")]
  [InlineData("report", "42", "viewer", "user", "")]
  public void Create_EmptyArguments_ThrowsArgumentException(
      string objectType, string objectId, string relation, string subjectType, string subjectId)
  {
    Assert.Throws<ArgumentException>(() =>
        RelationTuple.Create(objectType, objectId, relation, subjectType, subjectId));
  }

  [Fact]
  public void ToString_ReturnsZanzibarNotation()
  {
    var tuple = RelationTuple.Create("report", "42", "viewer", "user", "7");

    Assert.Equal("report:42#viewer@user:7", tuple.ToString());
  }

  [Fact]
  public void Create_WithSubjectRelation_IncludesInToString()
  {
    var tuple = RelationTuple.Create("report", "42", "viewer", "group", "finance", "member");

    Assert.Equal("report:42#viewer@group:finance#member", tuple.ToString());
  }

  [Theory]
  [InlineData("")]
  [InlineData("  ")]
  public void Create_BlankSubjectRelation_IsRejected(string subjectRelation) =>
      Assert.Throws<ArgumentException>(() => RelationTuple.Create(
          "report", "42", "viewer", "group", "finance", subjectRelation));

  [Fact]
  public void Create_RequiresFutureUtcExpiration()
  {
    Assert.Throws<ArgumentException>(() => RelationTuple.Create(
        "report", "42", "viewer", "user", "7", expiresAt: DateTime.Now.AddDays(1)));
    Assert.Throws<ArgumentException>(() => RelationTuple.Create(
        "report", "42", "viewer", "user", "7", expiresAt: DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified)));
    Assert.Throws<ArgumentException>(() => RelationTuple.Create(
        "report", "42", "viewer", "user", "7", expiresAt: DateTime.UtcNow.AddSeconds(-1)));
    Assert.NotNull(RelationTuple.Create(
        "report", "42", "viewer", "user", "7", expiresAt: DateTime.UtcNow.AddDays(1)).ExpiresAt);
    Assert.Null(RelationTuple.Create("report", "42", "viewer", "user", "7").ExpiresAt);
  }

  [Fact]
  public void Create_NormalizesExpirationToPostgreSqlMicroseconds()
  {
    var requested = DateTime.UtcNow.AddDays(1).AddTicks(7);
    var tuple = RelationTuple.Create("document", "42", "viewer", "user", "7",
        expiresAt: requested);
    Assert.Equal(0, tuple.ExpiresAt!.Value.Ticks % 10);
    Assert.True(tuple.ExpiresAt <= requested);
    Assert.True(tuple.ExpiresAt > requested.AddTicks(-10));
  }
}
