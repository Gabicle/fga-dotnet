using Permissions.Domain.ValueObjects;

namespace Permissions.Domain.Tests;

public sealed class TupleKeyTests
{
  [Fact]
  public void Constructor_ValidArguments_ReturnsTupleKey()
  {
    var key = new TupleKey("report", "42", "viewer", "user", "7");

    Assert.Equal("report", key.ObjectType);
    Assert.Equal("42", key.ObjectId);
    Assert.Equal("viewer", key.Relation);
    Assert.Equal("user", key.SubjectType);
    Assert.Equal("7", key.SubjectId);
  }

  [Fact]
  public void ToString_ReturnsZanzibarNotation()
  {
    var key = new TupleKey("report", "42", "viewer", "user", "7");

    Assert.Equal("report:42#viewer@user:7", key.ToString());
  }

  [Theory]
  [InlineData("", "42", "viewer", "user", "7")]
  [InlineData("report", "", "viewer", "user", "7")]
  [InlineData("report", "42", "", "user", "7")]
  [InlineData("report", "42", "viewer", "", "7")]
  [InlineData("report", "42", "viewer", "user", "")]
  public void Constructor_EmptyArguments_ThrowsArgumentException(
      string objectType, string objectId, string relation, string subjectType, string subjectId)
  {
    Assert.Throws<ArgumentException>(() =>
        new TupleKey(objectType, objectId, relation, subjectType, subjectId));
  }

  [Fact]
  public void Equality_SameValues_AreEqual()
  {
    var a = new TupleKey("report", "42", "viewer", "user", "7");
    var b = new TupleKey("report", "42", "viewer", "user", "7");

    Assert.Equal(a, b);
  }

  [Fact]
  public void Equality_DifferentValues_AreNotEqual()
  {
    var a = new TupleKey("report", "42", "viewer", "user", "7");
    var b = new TupleKey("report", "42", "editor", "user", "7");

    Assert.NotEqual(a, b);
  }
}