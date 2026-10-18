using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Policies;
using Permissions.Application.Services;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Tests;

public sealed class TupleGrantServiceTests
{
  private readonly Mock<IRelationTupleRepository> _tuples = new();

  private PermissionService Service() => new(_tuples.Object,
      new PermissionCheckEngine(_tuples.Object, new Mock<IRoleRepository>().Object),
      new AbacEvaluator([]));

  [Theory]
  [InlineData(TupleGrantOutcome.Success)]
  [InlineData(TupleGrantOutcome.ActiveExpirationConflict)]
  [InlineData(TupleGrantOutcome.ExpirationElapsed)]
  public async Task GrantMapsDatabaseOutcome(TupleGrantOutcome outcome)
  {
    _tuples.Setup(r => r.GrantAsync(It.IsAny<RelationTuple>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(outcome);
    var grant = new GrantPermissionRequest("user", "7", "viewer", "document", "42");
    if (outcome == TupleGrantOutcome.Success)
      await Service().GrantAsync(grant);
    else if (outcome == TupleGrantOutcome.ActiveExpirationConflict)
      await Assert.ThrowsAsync<TupleGrantConflictException>(() => Service().GrantAsync(grant));
    else
      await Assert.ThrowsAsync<ArgumentException>(() => Service().GrantAsync(grant));
    _tuples.Verify(r => r.GrantAsync(It.IsAny<RelationTuple>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task RevokePassesFullSixFieldKey()
  {
    var key = new TupleKey("document", "42", "viewer", "group", "finance", "member");
    await Service().RevokeAsync(key);
    _tuples.Verify(r => r.DeleteAsync(It.Is<TupleKey>(actual => actual == key),
        It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task UnknownGrantOutcomeCannotBeReportedAsSuccess()
  {
    _tuples.Setup(r => r.GrantAsync(It.IsAny<RelationTuple>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((TupleGrantOutcome)99);
    await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GrantAsync(
        new GrantPermissionRequest("user", "7", "viewer", "document", "42")));
  }
}
